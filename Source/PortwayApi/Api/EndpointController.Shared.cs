using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using System.Data.Common;
using PortwayApi.Services.Providers;

using Dapper;
using System.Data;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Xml.Linq;
using PortwayApi.Classes;
using PortwayApi.Helpers;
using PortwayApi.Interfaces;
using PortwayApi.Services;
using PortwayApi.Services.Files;
using Serilog;
using System.Runtime.CompilerServices;

namespace PortwayApi.Api;

public partial class EndpointController
{
    /// <summary>Resolves an endpoint via the central resolver; returns an error result when not found</summary>
    private IActionResult? TryResolveEndpoint(
        EndpointType type,
        string endpointName,
        string? namespaceName,
        out EndpointDefinition endpoint,
        string? notFoundMessage = null)
    {
        if (_endpointResolver.TryResolve(type, namespaceName, endpointName, out var found))
        {
            endpoint = found!;

            // Resolved but switched off: report the outage instead of serving
            if (!endpoint.Enabled)
            {
                Log.Debug("Endpoint disabled: {EndpointName}", endpointName);
                return DisabledEndpoint.Result(this);
            }

            return null;
        }

        endpoint = null!;
        Log.Warning("Endpoint not found: {EndpointName}", endpointName);
        return PortwayResults.NotFound(notFoundMessage ?? $"Endpoint '{endpointName}' not found");
    }

    /// <summary>Central boundary for unexpected handler errors: logs and returns a masked response</summary>
    private IActionResult HandleUnexpectedError(
        Exception ex,
        string operation,
        string endpointName,
        string? responseDetail = null)
    {
        Log.Error(ex, "Error processing {Operation} for {Endpoint}", operation, endpointName);
        return PortwayResults.ServerError(HttpContext, responseDetail ?? "An error occurred while processing your request");
    }

    /// <summary>Central boundary returning masked ProblemDetails for unexpected dispatch errors</summary>
    private IActionResult HandleUnexpectedProblem(Exception ex, string operation)
    {
        Log.Error(ex, "Error processing {Operation} request for {Path}", operation, Request.Path);
        return PortwayResults.ServerError(HttpContext, "Error processing. Please check the logs for more details.");
    }

    /// <summary>Parses the catchall segment to determine endpoint type and name with namespace support</summary>
    private (EndpointType Type, string? Namespace, string Name, string? Id, string RemainingPath) ParseEndpoint(string catchall)
    {
        var segments = catchall.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return (EndpointType.Standard, null, string.Empty, null, string.Empty);

        Log.Debug("Parsing endpoint: Segments=[{Segments}]", string.Join(", ", segments));

        // Try to parse as namespaced endpoint first
        if (segments.Length >= 2)
        {
            var potentialNamespace = segments[0];
            var potentialEndpointRaw = segments[1];

            // Remove any OData-style key appended to the endpoint name (e.g. "Cancellations(123)" or "Cancellations(guid'...')")
            var potentialEndpoint = Regex.Replace(potentialEndpointRaw, @"\([^\)]*\)$", "");
            var namespacedKey = $"{potentialNamespace}/{potentialEndpoint}";

            // Check if this namespaced endpoint exists (using cleaned endpoint name)
            if (TryDetermineEndpointType(namespacedKey, out var endpointType))
            {
                string? id = null;
                string remainingPath = "";

                // If the endpoint part itself included the id (e.g. Cancellations(123) ) extract it
                if (potentialEndpointRaw != potentialEndpoint)
                {
                    // attempt to extract id from the parentheses in segment[1]
                    var segment = potentialEndpointRaw;
                    id = segment switch
                    {
                        // guid'...' form: Cancellations(guid'...') 
                        var s when Regex.IsMatch(s, @"^\w+\(guid'([\w\-]+)'\)$") =>
                            Regex.Match(s, @"^\w+\(guid'([\w\-]+)'\)$").Groups[1].Value,

                        // quoted string form: Cancellations('value')
                        var s when Regex.IsMatch(s, @"^\w+\('([^']+)'\)$") =>
                            Regex.Match(s, @"^\w+\('([^']+)'\)$").Groups[1].Value,

                        // plain GUID inside parentheses: Cancellations(744276de-4918-4b56-af75-16901371983b)
                        var s when Regex.IsMatch(s, @"^\w+\(([0-9a-fA-F\-]{36})\)$") =>
                            Regex.Match(s, @"^\w+\(([0-9a-fA-F\-]{36})\)$").Groups[1].Value,

                        // numeric key: Cancellations(123)
                        var s when Regex.IsMatch(s, @"^\w+\((\d+)\)$") =>
                            Regex.Match(s, @"^\w+\((\d+)\)$").Groups[1].Value,
                        _ => null
                    };
                }

                // If there's a third segment, it may contain ID or remaining path
                if (segments.Length > 2)
                {
                    var thirdSegment = segments[2];

                    // Extract ID if it matches expected patterns (only if we didn't already get id)
                    if (id == null)
                    {
                        id = thirdSegment switch
                        {
                            // Parenthesized formats
                            var seg when Regex.IsMatch(seg, @"^\(guid'([\w\-]+)'\)$") =>
                                Regex.Match(seg, @"^\(guid'([\w\-]+)'\)$").Groups[1].Value,
                            var seg when Regex.IsMatch(seg, @"^\('([^']+)'\)$") =>
                                Regex.Match(seg, @"^\('([^']+)'\)$").Groups[1].Value,
                            var seg when Regex.IsMatch(seg, @"^\(([0-9a-fA-F\-]{36})\)$") =>
                                Regex.Match(seg, @"^\(([0-9a-fA-F\-]{36})\)$").Groups[1].Value,
                            var seg when Regex.IsMatch(seg, @"^\((\d+)\)$") =>
                                Regex.Match(seg, @"^\((\d+)\)$").Groups[1].Value,

                            // Non-parenthesized formats 
                            var seg when Regex.IsMatch(seg, @"^guid'([\w\-]+)'$") =>
                                Regex.Match(seg, @"^guid'([\w\-]+)'$").Groups[1].Value,
                            var seg when Regex.IsMatch(seg, @"^'([^']+)'$") =>
                                Regex.Match(seg, @"^'([^']+)'$").Groups[1].Value,
                            var seg when Guid.TryParse(seg, out _) => seg,
                            var seg when Regex.IsMatch(seg, @"^\d+$") => seg,

                            _ => null
                        };

                        // Set remaining path if there are segments after the ID
                        if (id != null && segments.Length > 3)
                        {
                            remainingPath = string.Join('/', segments.Skip(3));
                        }
                        else if (id == null)
                        {
                            // third segment not an ID -> treat as remaining path
                            remainingPath = string.Join('/', segments.Skip(2));
                        }
                    }
                    else
                    {
                        // we already have id from segment[1], so third+ are remaining path
                        if (segments.Length > 2)
                        {
                            remainingPath = string.Join('/', segments.Skip(2));
                        }
                    }
                }

                Log.Debug("Namespaced endpoint found: {Namespace}/{Name}, Type={Type}, ID={Id}",
                    potentialNamespace, potentialEndpoint, endpointType, id);

                return (endpointType, potentialNamespace, potentialEndpoint, id, remainingPath);
            }
        }

        // Fallback to traditional parsing (backward compatibility)
        string endpointName = segments[0];
        string? fallbackId = null;
        string fallbackRemainingPath = segments.Length > 1 ? string.Join('/', segments.Skip(1)) : string.Empty;

        Log.Debug("Fallback to traditional parsing: '{EndpointName}', RemainingPath='{RemainingPath}'",
            endpointName, fallbackRemainingPath);

        // Extract ID from endpoint name (legacy format)
        fallbackId = endpointName switch
        {
            var name when Regex.IsMatch(name, @"^\w+\(guid'([\w\-]+)'\)$") =>
                Regex.Match(name, @"^\w+\(guid'([\w\-]+)'\)$").Groups[1].Value,
            var name when Regex.IsMatch(name, @"^\w+\('([^']+)'\)$") =>
                Regex.Match(name, @"^\w+\('([^']+)'\)$").Groups[1].Value,
            // plain GUID inside parentheses fallback
            var name when Regex.IsMatch(name, @"^\w+\(([0-9a-fA-F\-]{36})\)$") =>
                Regex.Match(name, @"^\w+\(([0-9a-fA-F\-]{36})\)$").Groups[1].Value,
            var name when Regex.IsMatch(name, @"^\w+\((\d+)\)$") =>
                Regex.Match(name, @"^\w+\((\d+)\)$").Groups[1].Value,
            _ => null
        };

        // Clean endpoint name if ID was extracted
        if (fallbackId != null)
        {
            endpointName = Regex.Replace(endpointName, @"\([^)]+\)$", "");
        }

        // Determine endpoint type using pattern matching
        var fallbackEndpointType = DetermineEndpointType(endpointName);

        Log.Debug("Final parsed endpoint: Type={Type}, Name={Name}, ID={Id}",
            fallbackEndpointType, endpointName, fallbackId);

        return (fallbackEndpointType, null, endpointName, fallbackId, fallbackRemainingPath);
    }

    /// <summary>Resolves existence and endpoint type in one pass; probe order decides ties, so it lives here only</summary>
    private static bool TryDetermineEndpointType(string key, out EndpointType type)
    {
        if (EndpointHandler.GetSqlEndpoints().ContainsKey(key))
        {
            type = EndpointType.SQL;
            return true;
        }

        if (EndpointHandler.GetSqlWebhookEndpoints().ContainsKey(key))
        {
            type = EndpointType.Webhook;
            return true;
        }

        if (EndpointHandler.GetProxyEndpoints().TryGetValue(key, out var proxy))
        {
            type = proxy.IsComposite ? EndpointType.Composite : EndpointType.Proxy;
            return true;
        }

        if (EndpointHandler.GetFileEndpoints().ContainsKey(key))
        {
            type = EndpointType.Files;
            return true;
        }

        if (EndpointHandler.GetStaticEndpoints().ContainsKey(key))
        {
            type = EndpointType.Static;
            return true;
        }

        type = EndpointType.Standard;
        return false;
    }

    /// <summary>Determines endpoint type for a given key (supports both namespaced and non-namespaced)</summary>
    private static EndpointType DetermineEndpointType(string key)
    {
        if (key == "composite")
            return EndpointType.Composite;

        TryDetermineEndpointType(key, out var type);
        return type;
    }

    /// <summary>Replaces placeholders in the base directory with actual values</summary>
    private string ProcessBaseDirectory(string baseDirectory, string environment)
    {
        if (string.IsNullOrEmpty(baseDirectory))
            return string.Empty;
        
        // Replace {env} placeholder with actual environment
        var processedDirectory = baseDirectory.Replace("{env}", environment, StringComparison.OrdinalIgnoreCase);
        
        // Add support for additional placeholders if needed
        processedDirectory = processedDirectory.Replace("{date}", DateTime.UtcNow.ToString("yyyy-MM-dd"));
        processedDirectory = processedDirectory.Replace("{year}", DateTime.UtcNow.Year.ToString());
        processedDirectory = processedDirectory.Replace("{month}", DateTime.UtcNow.Month.ToString("00"));
        
        return processedDirectory;
    }

}
