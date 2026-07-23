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

/// <summary>Unified controller that handles all endpoint types (SQL, Proxy, Composite, Webhook)</summary>
[ApiController]
[Route("api")] // Base route only, we'll use action-level routing
public partial class EndpointController : ControllerBase
{
    private readonly EnvironmentSettings _environmentSettings;
    private readonly IEnvironmentSettingsProvider _environmentSettingsProvider;
    private readonly FileHandlerService _fileHandlerService;
    private readonly SqlConnectionPoolService _connectionPoolService;
    private readonly EndpointResolver _endpointResolver;
    private readonly CompositeRequestHandler _compositeRequestHandler;
    private readonly StaticRequestHandler _staticRequestHandler;
    private readonly SqlRequestHandler _sqlRequestHandler;
    private readonly ProxyRequestHandler _proxyRequestHandler;

    /// <summary>Validates if the environment is allowed both globally and for the specific endpoint</summary>
    private (bool IsAllowed, IActionResult? ErrorResponse) ValidateEnvironmentRestrictions(
        string env,
        string? namespaceName,
        string endpointName,
        EndpointType endpointType)
    {
        // First check if environment is in the globally allowed list
        if (!_environmentSettings.IsEnvironmentAllowed(env))
        {
            Log.Warning("Environment '{Env}' is not in the global allowed list.", env);
            return (false, PortwayResults.BadRequest(this, $"Environment '{env}' is not allowed."));
        }

        // Then check endpoint-specific environment restrictions
        List<string>? allowedEnvironments = null;
        
        switch (endpointType)
        {
            case EndpointType.SQL:
                var sqlEndpoints = EndpointHandler.GetSqlEndpoints();
                if (TryGetEndpoint(sqlEndpoints, namespaceName, endpointName, out var sqlEndpoint))
                {
                    allowedEnvironments = sqlEndpoint?.AllowedEnvironments;
                }
                break;
                
            case EndpointType.Proxy:
                var endpointDefinitions = EndpointHandler.GetProxyEndpoints();
                if (TryGetEndpoint(endpointDefinitions, namespaceName, endpointName, out var proxyEndpoint))
                {
                    allowedEnvironments = proxyEndpoint?.AllowedEnvironments;
                }
                break;
                
                case EndpointType.Webhook:
                    var webhookEndpoints = EndpointHandler.GetSqlWebhookEndpoints();
                    if (TryGetEndpoint(webhookEndpoints, namespaceName, endpointName, out var webhookEndpoint))
                    {
                        allowedEnvironments = webhookEndpoint?.AllowedEnvironments;
                    }
                    break;
                
            case EndpointType.Static:
                var staticEndpoints = EndpointHandler.GetStaticEndpoints();
                if (TryGetEndpoint(staticEndpoints, namespaceName, endpointName, out var staticEndpoint))
                {
                    allowedEnvironments = staticEndpoint?.AllowedEnvironments;
                }
                break;
                
            case EndpointType.Files:
                var fileEndpoints = EndpointHandler.GetFileEndpoints();
                if (TryGetEndpoint(fileEndpoints, namespaceName, endpointName, out var fileEndpoint))
                {
                    allowedEnvironments = fileEndpoint?.AllowedEnvironments;
                }
                break;
        }

        if (allowedEnvironments != null && 
            allowedEnvironments.Count > 0 &&
            !allowedEnvironments.Contains(env, StringComparer.OrdinalIgnoreCase))
        {
            Log.Warning("Environment '{Env}' is not allowed for endpoint '{Endpoint}'.", env, endpointName);
            return (false, PortwayResults.BadRequest(this, $"Environment '{env}' is not allowed for this endpoint."));
        }

        // Environment is allowed
        return (true, null);
    }
    public EndpointController(
        EnvironmentSettings environmentSettings,
        IEnvironmentSettingsProvider environmentSettingsProvider,
        SqlConnectionPoolService connectionPoolService,
        FileHandlerService fileHandlerService,
        EndpointResolver endpointResolver,
        CompositeRequestHandler compositeRequestHandler,
        StaticRequestHandler staticRequestHandler,
        SqlRequestHandler sqlRequestHandler,
        ProxyRequestHandler proxyRequestHandler)
    {
        _environmentSettings = environmentSettings;
        _environmentSettingsProvider = environmentSettingsProvider;
        _connectionPoolService = connectionPoolService;
        _fileHandlerService = fileHandlerService;
        _endpointResolver = endpointResolver;
        _compositeRequestHandler = compositeRequestHandler;
        _staticRequestHandler = staticRequestHandler;
        _sqlRequestHandler = sqlRequestHandler;
        _proxyRequestHandler = proxyRequestHandler;
    }

    /// <summary>Helper method to try resolving an endpoint with namespace-aware lookup</summary>
    /// <typeparam name="T">The type of endpoint entity</typeparam>
    /// <param name="endpoints">The dictionary of endpoints to search</param>
    /// <param name="namespaceName">The namespace from the parsed URL (can be null)</param>
    /// <param name="endpointName">The endpoint name</param>
    /// <param name="endpoint">The found endpoint (if any)</param>
    /// <returns>True if endpoint was found, false otherwise</returns>
    private bool TryGetEndpoint<T>(Dictionary<string, T> endpoints, string? namespaceName, string endpointName, out T? endpoint)
    {
        endpoint = default;
        
        // First try with namespace if provided
        if (!string.IsNullOrEmpty(namespaceName))
        {
            var namespacedKey = $"{namespaceName}/{endpointName}";
            if (endpoints.TryGetValue(namespacedKey, out endpoint))
            {
                return true;
            }
        }
        
        // Fallback to endpoint name only (backward compatibility)
        return endpoints.TryGetValue(endpointName, out endpoint);
    }

    /// <summary>Resolves namespace, endpoint name, and file id from a files catchall path</summary>
    private (string? Namespace, string EndpointName, string? FileId) ParseFileEndpointPath(string catchall)
    {
        var segments = catchall.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return (null, string.Empty, null);
        }

        var fileEndpoints = EndpointHandler.GetFileEndpoints();

        // Namespaced form: {namespace}/{endpoint}/{fileId}
        if (segments.Length >= 3 && fileEndpoints.ContainsKey($"{segments[0]}/{segments[1]}"))
        {
            return (segments[0], segments[1], segments[2]);
        }

        // Non-namespaced form: {endpoint}/{fileId}
        return (null, segments[0], segments.Length > 1 ? segments[1] : null);
    }

}
