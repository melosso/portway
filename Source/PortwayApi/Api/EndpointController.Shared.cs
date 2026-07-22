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
            return null;
        }

        endpoint = null!;
        Log.Warning("Endpoint not found: {EndpointName}", endpointName);
        return PortwayResults.NotFound(this, notFoundMessage ?? $"Endpoint '{endpointName}' not found");
    }

    /// <summary>Central boundary for unexpected handler errors: logs and returns a masked response</summary>
    private IActionResult HandleUnexpectedError(
        Exception ex,
        string operation,
        string endpointName,
        string? responseDetail = null)
    {
        Log.Error(ex, "Error processing {Operation} for {Endpoint}", operation, endpointName);
        return PortwayResults.ServerError(this, responseDetail ?? "An error occurred while processing your request");
    }

    /// <summary>Central boundary returning masked ProblemDetails for unexpected dispatch errors</summary>
    private IActionResult HandleUnexpectedProblem(Exception ex, string operation)
    {
        Log.Error(ex, "Error processing {Operation} request for {Path}", operation, Request.Path);
        return Problem(
            detail: "Error processing. Please check the logs for more details.",
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Error");
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
            if (NamespaceEndpointExists(namespacedKey))
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

                var endpointType = DetermineEndpointType(namespacedKey);

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

    /// <summary>Checks if a namespaced endpoint exists</summary>
    private bool NamespaceEndpointExists(string key)
    {
        return EndpointHandler.GetSqlEndpoints().ContainsKey(key) ||
               EndpointHandler.GetProxyEndpoints().ContainsKey(key) ||
               EndpointHandler.GetFileEndpoints().ContainsKey(key) ||
               EndpointHandler.GetStaticEndpoints().ContainsKey(key) ||
               EndpointHandler.GetSqlWebhookEndpoints().ContainsKey(key);
    }
    
    /// <summary>Determines endpoint type for a given key (supports both namespaced and non-namespaced)</summary>
    private EndpointType DetermineEndpointType(string key)
    {
        return key switch
        {
            "composite" => EndpointType.Composite,
            _ when EndpointHandler.GetSqlEndpoints().ContainsKey(key) => EndpointType.SQL,
            _ when EndpointHandler.GetSqlWebhookEndpoints().ContainsKey(key) => EndpointType.Webhook,
            _ when EndpointHandler.GetProxyEndpoints().ContainsKey(key) && 
                   EndpointHandler.GetProxyEndpoints()[key].IsComposite => EndpointType.Composite,
            _ when EndpointHandler.GetProxyEndpoints().ContainsKey(key) => EndpointType.Proxy,
            _ when EndpointHandler.GetFileEndpoints().ContainsKey(key) => EndpointType.Files,
            _ when EndpointHandler.GetStaticEndpoints().ContainsKey(key) => EndpointType.Static,
            _ => EndpointType.Standard
        };
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

    /// <summary>Resolves the storage path, supporting both relative and absolute BaseDirectory paths</summary>
    private string ResolveStoragePath(string baseDirectory, string environment, string filename)
    {
        // If BaseDirectory is an absolute path, use it directly
        if (!string.IsNullOrEmpty(baseDirectory) && Path.IsPathRooted(baseDirectory))
        {
            // For absolute paths, create subdirectory structure: AbsolutePath/Environment/Filename
            var environmentDir = Path.Combine(baseDirectory, environment);
            Directory.CreateDirectory(environmentDir);
            return Path.Combine(environmentDir, filename);
        }
        
        // For relative paths, use the existing logic
        if (!string.IsNullOrEmpty(baseDirectory))
        {
            filename = Path.Combine(baseDirectory, filename);
        }
        
        // Return the combined path with storage directory
        return filename;
    }


    /// <summary>Sets standard pagination headers on the response for consistency across endpoint types</summary>
    /// <param name="totalCount">Total number of items before pagination (if known)</param>
    /// <param name="returnedCount">Number of items being returned in this response</param>
    /// <param name="hasMore">Whether there are more items available</param>
    private void SetPaginationHeaders(int? totalCount, int returnedCount, bool hasMore = false)
    {
        if (totalCount.HasValue)
        {
            Response.Headers["X-Total-Count"] = totalCount.Value.ToString();
        }
        Response.Headers["X-Returned-Count"] = returnedCount.ToString();
        Response.Headers["X-Has-More"] = hasMore.ToString().ToLowerInvariant();
    }

    /// <summary>Sets Cache-Control header for GET responses</summary>
    /// <param name="maxAgeSeconds">Cache duration in seconds (default: 300 = 5 minutes)</param>
    /// <param name="isPublic">Whether the cache is public or private (default: public)</param>
    private void SetCacheControlHeader(int maxAgeSeconds = 300, bool isPublic = true)
    {
        var cacheType = isPublic ? "public" : "private";
        Response.Headers["Cache-Control"] = $"{cacheType}, max-age={maxAgeSeconds}";
    }












}
