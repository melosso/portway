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
/// <remarks>No [ProducesResponseType] here; catchall paths are stripped from the document, StandardErrorCodes is the source!!</remarks>
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
            return (false, PortwayResults.BadRequest($"Environment '{env}' is not allowed."));
        }

        // Then check endpoint-specific environment restrictions
        var allowedEnvironments = _endpointResolver.TryResolve(endpointType, namespaceName, endpointName, out var endpoint)
            ? endpoint?.AllowedEnvironments
            : null;

        if (allowedEnvironments != null &&
            allowedEnvironments.Count > 0 &&
            !allowedEnvironments.Contains(env, StringComparer.OrdinalIgnoreCase))
        {
            Log.Warning("Environment '{Env}' is not allowed for endpoint '{Endpoint}'.", env, endpointName);
            return (false, PortwayResults.BadRequest($"Environment '{env}' is not allowed for this endpoint."));
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
