using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;

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

/// <summary>
/// Unified controller that handles all endpoint types (SQL, Proxy, Composite, Webhook)
/// </summary>
[ApiController]
[Route("api")] // Base route only, we'll use action-level routing
public class EndpointController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly UrlValidator _urlValidator;
    private readonly EnvironmentSettings _environmentSettings;
    private readonly IODataToSqlConverter _oDataToSqlConverter;
    private readonly IEnvironmentSettingsProvider _environmentSettingsProvider;
    private readonly CompositeEndpointHandler _compositeHandler;
    private readonly FileHandlerService _fileHandlerService;
    private readonly SqlConnectionPoolService _connectionPoolService; 
    private readonly Services.Caching.CacheManager _cacheManager; 

    /// <summary>
    /// Validates if the environment is allowed both globally and for the specific endpoint
    /// </summary>
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
            return (false, BadRequest(new { error = $"Environment '{env}' is not allowed.", success = false }));
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
                var proxyEndpoints = EndpointHandler.GetEndpoints(
                    Path.Combine(Directory.GetCurrentDirectory(), "endpoints", "Proxy"));
                    
                if (TryGetEndpoint(proxyEndpoints, namespaceName, endpointName, out var proxyConfig))
                {
                    // Get the full endpoint definition to access AllowedEnvironments
                    var endpointDefinitions = EndpointHandler.GetProxyEndpoints();
                    if (TryGetEndpoint(endpointDefinitions, namespaceName, endpointName, out var proxyEndpoint))
                    {
                        allowedEnvironments = proxyEndpoint?.AllowedEnvironments;
                    }
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
            return (false, BadRequest(new { error = $"Environment '{env}' is not allowed for this endpoint.", success = false }));
        }

        // Environment is allowed
        return (true, null);
    }
    public EndpointController(
        IHttpClientFactory httpClientFactory,
        UrlValidator urlValidator,
        EnvironmentSettings environmentSettings,
        IODataToSqlConverter oDataToSqlConverter,
        IEnvironmentSettingsProvider environmentSettingsProvider,
        CompositeEndpointHandler compositeHandler,
        SqlConnectionPoolService connectionPoolService,
        Services.Caching.CacheManager cacheManager,
        FileHandlerService fileHandlerService)
    {
        _httpClientFactory = httpClientFactory;
        _urlValidator = urlValidator;
        _environmentSettings = environmentSettings;
        _oDataToSqlConverter = oDataToSqlConverter;
        _environmentSettingsProvider = environmentSettingsProvider;
        _compositeHandler = compositeHandler;
        _connectionPoolService = connectionPoolService;
        _cacheManager = cacheManager;
        _fileHandlerService = fileHandlerService;
    }

    /// <summary>
    /// Helper method to try resolving an endpoint with namespace-aware lookup
    /// </summary>
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

    /// <summary>
    /// Handles GET requests to endpoints
    /// </summary>
    [HttpGet("{env}/{**catchall}")]
    [ResponseCache(Duration = 300, VaryByQueryKeys = new[] { "*" })]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status405MethodNotAllowed)]
    [ProducesResponseType(StatusCodes.Status406NotAcceptable)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAsync(
        string env,
        string catchall,
        [FromQuery(Name = "$select")] string? select = null,
        [FromQuery(Name = "$filter")] string? filter = null,
        [FromQuery(Name = "$orderby")] string? orderby = null,
        [FromQuery(Name = "$top")] int top = 10,
        [FromQuery(Name = "$skip")] int skip = 0)
    {
        try
        {
            // Process the catchall to determine what type of endpoint we're dealing with
            var (endpointType, namespaceName, endpointName, id, remainingPath) = ParseEndpoint(catchall);
            var _allowedEnvironments = new List<string>();
            Log.Debug("Processing {Type} endpoint: {Namespace}/{Name}", endpointType, namespaceName ?? "None", endpointName);

            // Check environment restrictions
            var (isAllowed, errorResponse) = ValidateEnvironmentRestrictions(env, namespaceName, endpointName, endpointType);
            if (!isAllowed)
            {
                return errorResponse!;
            }

            switch (endpointType)
            {
                case EndpointType.SQL:
                    // Build the full key for lookup
                    var sqlKey = !string.IsNullOrEmpty(namespaceName) ? $"{namespaceName}/{endpointName}" : endpointName;
                    return await HandleSqlGetRequest(env, sqlKey, id, remainingPath, select, filter, orderby, top, skip);
                case EndpointType.Proxy:
                    // Build the full key for lookup
                    var proxyKey = !string.IsNullOrEmpty(namespaceName) ? $"{namespaceName}/{endpointName}" : endpointName;
                    return await HandleProxyRequest(env, proxyKey, id, remainingPath, "GET");
                case EndpointType.Static:
                    // Build the full key for lookup
                    var staticKey = !string.IsNullOrEmpty(namespaceName) ? $"{namespaceName}/{endpointName}" : endpointName;
                    return await HandleStaticGetRequest(env, staticKey, select, filter, orderby, top, skip);
                case EndpointType.Composite:
                    // Log warning and return 405
                    Log.Warning("Composite endpoints don't support GET requests");
                    return StatusCode(405, new { error = "Method not allowed" });
                case EndpointType.Webhook:
                    // Log warning and return 405
                    Log.Warning("Webhook endpoints don't support GET requests");
                    return StatusCode(405, new { error = "Method not allowed" });
                default:
                    // Log warning and return 404
                    Log.Warning("Unknown endpoint type for {EndpointName}", endpointName);
                    return NotFound(new { error = $"Endpoint '{endpointName}' not found", success = false });
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error processing GET request for {Path}", Request.Path);
            return Problem(
                detail: $"Error processing. Please check the logs for more details.",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Error"
            );
        }
    }

    /// <summary>
    /// Handles HEAD requests to static endpoints
    /// </summary>
    [HttpHead("{env}/{**catchall}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status405MethodNotAllowed)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public IActionResult HeadAsync(string env, string catchall)
    {
        try
        {
            // Process the catchall to determine endpoint type
            var (endpointType, namespaceName, endpointName, id, remainingPath) = ParseEndpoint(catchall);
            
            // Only support HEAD for static endpoints
            if (endpointType != EndpointType.Static)
            {
                return StatusCode(405, new { error = "HEAD method is only supported for static endpoints" });
            }

            Log.Debug("HEAD request for static endpoint: {Name}", endpointName);

            // Check environment restrictions
            var (isAllowed, errorResponse) = ValidateEnvironmentRestrictions(env, namespaceName, endpointName, endpointType);
            if (!isAllowed)
            {
                return errorResponse!;
            }

            // Get static endpoint definition
            var staticEndpoints = EndpointHandler.GetStaticEndpoints();
            // For static endpoints, build the full key for lookup
            var staticKey = !string.IsNullOrEmpty(namespaceName) ? $"{namespaceName}/{endpointName}" : endpointName;
            if (!staticEndpoints.TryGetValue(staticKey, out var endpoint))
            {
                return NotFound();
            }

            // Build path to content file - handle namespaced endpoints
            string endpointPath;
            if (endpoint.HasNamespace)
            {
                // For namespaced endpoints, use the full namespace/endpoint structure
                endpointPath = Path.Combine(Directory.GetCurrentDirectory(), "endpoints", "Static", 
                    endpoint.EffectiveNamespace!, endpoint.FolderName ?? endpointName);
            }
            else
            {
                // For non-namespaced endpoints, use just the endpoint name
                endpointPath = Path.Combine(Directory.GetCurrentDirectory(), "endpoints", "Static", endpointName);
            }
            
            var contentFile = endpoint.Properties!["ContentFile"].ToString()!;
            var contentFilePath = Path.Combine(endpointPath, contentFile);

            if (!System.IO.File.Exists(contentFilePath))
            {
                return NotFound();
            }

            // Get content type and file info
            var contentType = endpoint.Properties["ContentType"].ToString();
            
            // Auto-detect content type if not specified
            if (string.IsNullOrEmpty(contentType))
            {
                contentType = GetContentTypeFromExtension(contentFile);
                Log.Debug("Auto-detected content type: {ContentType} for file: {ContentFile}", contentType, contentFile);
            }
            
            var fileInfo = new FileInfo(contentFilePath);

            // Set headers without returning body
            Response.Headers["Content-Type"] = contentType;
            Response.Headers["Content-Length"] = fileInfo.Length.ToString();
            Response.Headers["Last-Modified"] = fileInfo.LastWriteTimeUtc.ToString("R");

            Log.Debug("HEAD response for static content: {Endpoint} ({ContentType})", endpointName, contentType);
            
            return Ok();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error processing HEAD request for {Path}", Request.Path);
            return StatusCode(500);
        }
    }

    /// <summary>
    /// Handles POST requests to endpoints
    /// </summary>
    [HttpPost("{env}/{**catchall}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status405MethodNotAllowed)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> PostAsync(
        string env,
        string catchall)
    {
        try
        {
            // Process the catchall to determine what type of endpoint we're dealing with
            var (endpointType, namespaceName, endpointName, id, remainingPath) = ParseEndpoint(catchall);
            
            Log.Debug("Processing {Type} endpoint: {Name} for POST", endpointType, endpointName);

            // Check environment restrictions
            var (isAllowed, errorResponse) = ValidateEnvironmentRestrictions(env, namespaceName, endpointName, endpointType);
            if (!isAllowed)
            {
                return errorResponse!;
            }

            // Read the request body - we'll need it for several endpoint types
            Request.EnableBuffering();
            string requestBody;
            using (var reader = new StreamReader(Request.Body, leaveOpen: true))
            {
                requestBody = await reader.ReadToEndAsync();
            }
            // Reset position for further reading if needed
            Request.Body.Position = 0;
            
            switch (endpointType)
            {
                case EndpointType.SQL:
                    var data = JsonSerializer.Deserialize<JsonElement>(requestBody);
                    // For SQL endpoints, build the full key for lookup (same as GET)
                    var sqlKey = !string.IsNullOrEmpty(namespaceName) ? $"{namespaceName}/{endpointName}" : endpointName;
                    return await HandleSqlPostRequest(env, sqlKey, data);
                    
                case EndpointType.Proxy:
                    // For proxy endpoints, build the full key for lookup
                    var proxyKey = !string.IsNullOrEmpty(namespaceName) ? $"{namespaceName}/{endpointName}" : endpointName;
                    return await HandleProxyRequest(env, proxyKey, id, remainingPath, "POST");
                    
                case EndpointType.Composite:
                    // For composite endpoints, build the full key for lookup
                    var compositeKey = !string.IsNullOrEmpty(namespaceName) ? $"{namespaceName}/{endpointName}" : endpointName;
                    string actualCompositeName = compositeKey.Replace("composite/", "");
                    return await HandleCompositeRequest(env, actualCompositeName, requestBody);
                    
                case EndpointType.Webhook:
                    // For webhook endpoints, build the full key for lookup
                    var webhookKey = !string.IsNullOrEmpty(namespaceName) ? $"{namespaceName}/{endpointName}" : endpointName;
                    string webhookId = remainingPath.Split('/')[0];
                    var webhookData = JsonSerializer.Deserialize<JsonElement>(requestBody);
                    return await HandleWebhookRequest(env, webhookKey, webhookId, webhookData);
                    
                default:
                    Log.Warning("Unknown endpoint type for {EndpointName}", endpointName);
                    return NotFound(new { error = $"Endpoint '{endpointName}' not found", success = false });
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error processing POST request for {Path}", Request.Path);
            return Problem(
                detail: $"Error processing. Please check the logs for more details.",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Error"
            );
        }
    }

    /// <summary>
    /// Handles PUT requests to endpoints
    /// </summary>
    [HttpPut("{env}/{**catchall}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status405MethodNotAllowed)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> PutAsync(
        string env,
        string catchall)
    {
        try
        {
            // Process the catchall to determine what type of endpoint we're dealing with
            var (endpointType, namespaceName, endpointName, id, remainingPath) = ParseEndpoint(catchall);
            
            Log.Debug("Processing {Type} endpoint: {Name} for PUT", endpointType, endpointName);

            // Check environment restrictions
            var (isAllowed, errorResponse) = ValidateEnvironmentRestrictions(env, namespaceName, endpointName, endpointType);
            if (!isAllowed)
            {
                return errorResponse!;
            }

            // Read the request body for SQL endpoint
            if (endpointType == EndpointType.SQL)
            {
                string requestBody;
                using (var reader = new StreamReader(Request.Body))
                {
                    requestBody = await reader.ReadToEndAsync();
                }
                
                var data = JsonSerializer.Deserialize<JsonElement>(requestBody);
                // For SQL endpoints, build the full key for lookup (same as GET)
                var sqlKey = !string.IsNullOrEmpty(namespaceName) ? $"{namespaceName}/{endpointName}" : endpointName;
                return await HandleSqlPutRequest(env, sqlKey, data);
            }
            else if (endpointType == EndpointType.Proxy)
            {
                // For proxy endpoints, build the full key for lookup
                var proxyKey = !string.IsNullOrEmpty(namespaceName) ? $"{namespaceName}/{endpointName}" : endpointName;
                return await HandleProxyRequest(env, proxyKey, id, remainingPath, "PUT");
            }
            else
            {
                // Composite and Webhook endpoints don't support PUT
                Log.Warning("{Type} endpoints don't support PUT requests", endpointType);
                return StatusCode(405, new { error = $"Method not allowed" });
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error processing PUT request for {Path}", Request.Path);
            return Problem(
                detail: $"Error processing. Please check the logs for more details.",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Error"
            );
        }
    }

    /// <summary>
    /// Handles DELETE requests to endpoints
    /// </summary>
    [HttpDelete("{env}/{**catchall}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status405MethodNotAllowed)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeleteAsync(
        string env,
        string catchall,
        [FromQuery] string? id = null)
    {
        try
        {
            // Process the catchall to determine what type of endpoint we're dealing with
            var (endpointType, namespaceName, endpointName, parsedId, remainingPath) = ParseEndpoint(catchall);
            
            Log.Debug("Processing {Type} endpoint: {Name} for DELETE", endpointType, endpointName);

            // Check environment restrictions
            var (isAllowed, errorResponse) = ValidateEnvironmentRestrictions(env, namespaceName, endpointName, endpointType);
            if (!isAllowed)
            {
                return errorResponse!;
            }

            switch (endpointType)
            {
                case EndpointType.SQL:
                    // Ensure ID is provided for SQL DELETE
                    if (string.IsNullOrEmpty(parsedId))
                    {
                        return BadRequest(new { 
                            error = "ID parameter is required for delete operations", 
                            success = false 
                        });
                    }
                    
                    // For SQL endpoints, build the full key for lookup (same as GET)
                    var sqlKey = !string.IsNullOrEmpty(namespaceName) ? $"{namespaceName}/{endpointName}" : endpointName;
                    return await HandleSqlDeleteRequest(env, sqlKey, parsedId);
                    
                case EndpointType.Proxy:
                    // For proxy endpoints, build the full key for lookup
                    var proxyKey = !string.IsNullOrEmpty(namespaceName) ? $"{namespaceName}/{endpointName}" : endpointName;
                    return await HandleProxyRequest(env, proxyKey, parsedId, remainingPath, "DELETE");
                    
                case EndpointType.Composite:
                    Log.Warning("Composite endpoints don't support DELETE requests");
                    return StatusCode(405, new { error = "Method not allowed" });
                    
                case EndpointType.Webhook:
                    Log.Warning("{Type} endpoints don't support DELETE requests", endpointType);
                    return StatusCode(405, new { error = $"Method not allowed" });
                    
                default:
                    Log.Warning("Unknown endpoint type for {EndpointName}", endpointName);
                    return NotFound(new { error = $"Endpoint '{endpointName}' not found", success = false });
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error processing DELETE request for {Path}", Request.Path);
            return Problem(
                detail: $"Error processing. Please check the logs for more details.",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Error"
            );
        }
    }

    /// <summary>
    /// Handles PATCH requests to endpoints
    /// </summary>
    [HttpPatch("{env}/{**catchall}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> PatchAsync(
        string env,
        string catchall)
    {
        try
        {
            // Process the catchall to determine what type of endpoint we're dealing with
            var (endpointType, namespaceName, endpointName, id, remainingPath) = ParseEndpoint(catchall);
            
            Log.Debug("Processing {Type} endpoint: {Name} for PATCH", endpointType, endpointName);

            // Check environment restrictions
            var (isAllowed, errorResponse) = ValidateEnvironmentRestrictions(env, namespaceName, endpointName, endpointType);
            if (!isAllowed)
            {
                return errorResponse!;
            }

            // Handle different endpoint types
            if (endpointType == EndpointType.Proxy)
            {
                var proxyKey = !string.IsNullOrEmpty(namespaceName) ? $"{namespaceName}/{endpointName}" : endpointName;
                return await HandleProxyRequest(env, proxyKey, id, remainingPath, "PATCH");
            }
            else if (endpointType == EndpointType.SQL)
            {
                // Read the request body
                using var requestBody = await JsonDocument.ParseAsync(Request.Body);
                return await HandleSqlPatchRequest(env, endpointName, requestBody);
            }
            
            Log.Warning("{Type} endpoints don't support PATCH requests", endpointType);
            return StatusCode(405, new { error = $"Method not allowed" });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error processing PATCH request for {Path}", Request.Path);
            return Problem(
                detail: $"Error processing. Please check the logs for more details.",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Error"
            );
        }
    }

    /// <summary>
    /// Handle file uploads
    /// </summary>
    [HttpPost("{env}/files/{**catchall}")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UploadFileAsync(
        string env,
        string catchall,
        [FromForm] IFormFile file,
        [FromQuery] bool overwrite = false)
    {
        try
        {
            // Parse the catchall to extract namespace and endpoint information for files
            string endpointName;
            string? namespaceName = null;
            string? subpath = null;
            
            var segments = catchall.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                return BadRequest(new { error = "Missing endpoint name in the URL path", success = false });
            }
            
            // Check if we have namespace/endpoint format (2+ segments)
            if (segments.Length >= 2)
            {
                // Could be namespace/endpoint/subpath or just endpoint/subpath
                // Try to determine if first segment is a namespace by checking if namespace/endpoint exists
                var potentialNamespace = segments[0];
                var potentialEndpoint = segments[1];
                var allFileEndpoints = EndpointHandler.GetFileEndpoints();
                
                // Check if namespace/endpoint key exists
                if (allFileEndpoints.ContainsKey($"{potentialNamespace}/{potentialEndpoint}"))
                {
                    namespaceName = potentialNamespace;
                    endpointName = potentialEndpoint;
                    if (segments.Length > 2)
                    {
                        subpath = string.Join('/', segments.Skip(2));
                    }
                }
                else
                {
                    // Fallback to treating first segment as endpoint name
                    endpointName = segments[0];
                    if (segments.Length > 1)
                    {
                        subpath = string.Join('/', segments.Skip(1));
                    }
                }
            }
            else
            {
                endpointName = segments[0];
            }
            
            // Check if this endpoint exists
            var fileEndpoints = EndpointHandler.GetFileEndpoints();
            if (!TryGetEndpoint(fileEndpoints, namespaceName, endpointName, out var endpoint))
            {
                return NotFound(new { error = $"Endpoint '{endpointName}' not found", success = false });
            }
            
            // Check environment restrictions
            var (isAllowed, errorResponse) = ValidateEnvironmentRestrictions(env, namespaceName, endpointName, EndpointType.Files);
            if (!isAllowed)
            {
                return errorResponse!;
            }
            
            // Validate file
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { error = "No file was uploaded", success = false });
            }
            
            // Get storage options from endpoint definition
            var baseDirectory = endpoint?.Properties != null && endpoint.Properties.TryGetValue("BaseDirectory", out var baseDirObj) 
                ? baseDirObj?.ToString() ?? string.Empty
                : string.Empty;

            // PROCESS THE BASE DIRECTORY TO REPLACE PLACEHOLDERS
            baseDirectory = ProcessBaseDirectory(baseDirectory, env);
                
            var allowedExtensions = endpoint?.Properties != null && endpoint.Properties.TryGetValue("AllowedExtensions", out var extensionsObj) 
                && extensionsObj is List<string> extensions
                ? extensions
                : new List<string>();
            
            // Construct the target filename
            string filename = file.FileName;
            
            // Add subpath if provided
            if (!string.IsNullOrEmpty(subpath))
            {
                filename = Path.Combine(subpath, filename);
            }
            
            // Add base directory if configured
            if (!string.IsNullOrEmpty(baseDirectory))
            {
                filename = Path.Combine(baseDirectory, filename);
            }
            
            // Normalize path separators
            filename = filename.Replace('\\', '/');
            
            // Validate file extension
            string extension = Path.GetExtension(filename).ToLowerInvariant();
            if (allowedExtensions.Count > 0 && !allowedExtensions.Contains(extension))
            {
                return StatusCode(415, new { 
                    error = $"Files with extension {extension} are not allowed for this endpoint",
                    allowedExtensions = allowedExtensions,
                    code = "UNSUPPORTED_FILE_TYPE"
                });
            }
            
            // Upload the file
            using var stream = file.OpenReadStream();
            string fileId;
            
            // Check if we should use absolute path handling
            if (!string.IsNullOrEmpty(baseDirectory) && Path.IsPathRooted(baseDirectory))
            {
                // For absolute paths, construct the full path
                string absoluteFilePath = filename;
                if (!string.IsNullOrEmpty(subpath))
                {
                    absoluteFilePath = Path.Combine(subpath, file.FileName);
                }
                absoluteFilePath = Path.Combine(baseDirectory, absoluteFilePath);
                
                // Use the absolute path upload method
                fileId = await _fileHandlerService.UploadFileToAbsolutePathAsync(env, absoluteFilePath, stream, baseDirectory, overwrite);
            }
            else
            {
                // Use the standard relative path upload method
                fileId = await _fileHandlerService.UploadFileAsync(env, filename, stream, overwrite);
            }
            
            // Return success with file info
            return Created($"/api/{env}/files/{endpointName}/{fileId}", new { 
                success = true, 
                fileId = fileId, 
                filename = filename,
                contentType = file.ContentType,
                size = file.Length,
                url = $"/api/{env}/files/{endpointName}/{fileId}" 
            });
        }
        catch (ArgumentException ex)
        {
            // File validation errors
            return BadRequest(new { error = ex.Message, success = false });
        }
        catch (InvalidOperationException ex)
        {
            // File already exists errors
            return Conflict(new { error = ex.Message, code = "FILE_ALREADY_EXISTS" });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error uploading file for {Path}", Request.Path);
            return StatusCode(500, new { error = "An error occurred while uploading the file" });
        }
    }

    /// <summary>
    /// Handle file downloads
    /// </summary>
    [HttpGet("{env}/files/{**catchall}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status206PartialContent)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status416RangeNotSatisfiable)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DownloadFileAsync(
        string env,
        string catchall)
    {
        try
        {
            // Extract the endpoint name and file ID from the catchall
            var segments = catchall.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2)
            {
                return BadRequest(new { error = "Missing endpoint name or file ID in the URL path", success = false });
            }
            
            string endpointName = segments[0];
            string fileId = segments[1];
            
            // Check if this endpoint exists
            var fileEndpoints = EndpointHandler.GetFileEndpoints();
            if (!fileEndpoints.TryGetValue(endpointName, out var endpoint))
            {
                return NotFound(new { error = $"Endpoint '{endpointName}' not found", success = false });
            }
            
            // Check environment restrictions
            var (isAllowed, errorResponse) = ValidateEnvironmentRestrictions(env, null, endpointName, EndpointType.Files);
            if (!isAllowed)
            {
                return errorResponse!;
            }
            
            // Download the file
            var (fileStream, filename, contentType) = await _fileHandlerService.DownloadFileAsync(fileId);
            
            // Return the file
            return File(fileStream, contentType, filename);
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new { error = $"File not found: {ex.FileName}", success = false });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message, success = false });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error downloading file for {Path}", Request.Path);
            return StatusCode(500, new { error = "An error occurred while downloading the file" });
        }
    }

    /// <summary>
    /// Handle file deletions
    /// </summary>
    [HttpDelete("{env}/files/{**catchall}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeleteFileAsync(
        string env,
        string catchall)
    {
        try
        {
            // Extract the endpoint name and file ID from the catchall
            var segments = catchall.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2)
            {
                return BadRequest(new { error = "Missing endpoint name or file ID in the URL path", success = false });
            }
            
            string endpointName = segments[0];
            string fileId = segments[1];
            
            // Check if this endpoint exists
            var fileEndpoints = EndpointHandler.GetFileEndpoints();
            if (!fileEndpoints.TryGetValue(endpointName, out var endpoint))
            {
                return NotFound(new { error = $"Endpoint '{endpointName}' not found", success = false });
            }
            
            // Check environment restrictions
            var (isAllowed, errorResponse) = ValidateEnvironmentRestrictions(env, null, endpointName, EndpointType.Files);
            if (!isAllowed)
            {
                return errorResponse!;
            }
            
            // Delete the file
            await _fileHandlerService.DeleteFileAsync(fileId);
            
            // Return success
            return Ok(new { 
                success = true, 
                message = "File deleted successfully" 
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message, success = false });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error deleting file for {Path}", Request.Path);
            return StatusCode(500, new { error = "An error occurred while deleting the file" });
        }
    }

    /// <summary>
    /// List files in an endpoint
    /// </summary>
    [HttpGet("{env}/files/{endpointName}/list")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ListFilesAsync(
        string env,
        string endpointName,
        [FromQuery] string? prefix = null)
    {
        try
        {
            // Check if this endpoint exists
            var fileEndpoints = EndpointHandler.GetFileEndpoints();
            if (!fileEndpoints.TryGetValue(endpointName, out var endpoint))
            {
                return NotFound(new { error = $"Endpoint '{endpointName}' not found", success = false });
            }
            
            // Check environment restrictions
            var (isAllowed, errorResponse) = ValidateEnvironmentRestrictions(env, null, endpointName, EndpointType.Files);
            if (!isAllowed)
            {
                return errorResponse!;
            }
            
            // Get base directory for this endpoint
            var baseDirectory = (endpoint.Properties != null && endpoint.Properties.TryGetValue("BaseDirectory", out var baseDirObj)) 
                ? baseDirObj?.ToString() ?? string.Empty
                : string.Empty;

            // PROCESS THE BASE DIRECTORY TO REPLACE PLACEHOLDERS
            baseDirectory = ProcessBaseDirectory(baseDirectory, env);
                
            // Prepare the prefix by combining base directory and provided prefix
            if (!string.IsNullOrEmpty(baseDirectory))
            {
                if (string.IsNullOrEmpty(prefix))
                {
                    prefix = baseDirectory;
                }
                else
                {
                    prefix = Path.Combine(baseDirectory, prefix).Replace('\\', '/');
                }
            }
            
            // List the files
            var files = await _fileHandlerService.ListFilesAsync(env, prefix);
            
            // Add download URLs
            var filesWithUrls = files.Select(f => new
            {
                fileId = f.FileId,
                fileName = f.FileName,
                contentType = f.ContentType,
                size = f.Size,
                lastModified = f.LastModified,
                url = $"/api/{env}/files/{endpointName}/{f.FileId}",
                isInMemoryOnly = f.IsInMemoryOnly
            });
            
            // Return the list
            return Ok(new { 
                success = true, 
                files = filesWithUrls,
                count = filesWithUrls.Count() 
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error listing files for {Path}", Request.Path);
            return StatusCode(500, new { error = "An error occurred while listing files" });
        }
    }

    #region Helper Methods and Handlers

    /// <summary>
    /// Parses the catchall segment to determine endpoint type and name with namespace support
    /// </summary>
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

    /// <summary>
    /// Checks if a namespaced endpoint exists
    /// </summary>
    private bool NamespaceEndpointExists(string key)
    {
        return EndpointHandler.GetSqlEndpoints().ContainsKey(key) ||
               EndpointHandler.GetProxyEndpoints().ContainsKey(key) ||
               EndpointHandler.GetFileEndpoints().ContainsKey(key) ||
               EndpointHandler.GetStaticEndpoints().ContainsKey(key);
    }
    
    /// <summary>
    /// Determines endpoint type for a given key (supports both namespaced and non-namespaced)
    /// </summary>
    private EndpointType DetermineEndpointType(string key)
    {
        return key switch
        {
            "composite" => EndpointType.Composite,
            "webhook" => EndpointType.Webhook,
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

    /// <summary>
    /// Replaces placeholders in the base directory with actual values
    /// </summary>
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

    /// <summary>
    /// Resolves the storage path, supporting both relative and absolute BaseDirectory paths
    /// </summary>
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

    /// <summary>
    /// Handles proxy requests for any HTTP method with request caching
    /// </summary>
    private static readonly MemoryCache _proxyCache = new MemoryCache(new MemoryCacheOptions());
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _cacheLocks = new ConcurrentDictionary<string, SemaphoreSlim>();

    private async Task<IActionResult> HandleProxyRequest(
        string env,
        string endpointName,
        string? id,
        string remainingPath,
        string method)
    {
        Log.Debug("Handling proxy request: {Endpoint} {Method}", endpointName, method);

        try
        {
            // Load proxy endpoints
            var proxyEndpoints = EndpointHandler.GetProxyEndpoints();

            // Find the endpoint configuration
            if (!proxyEndpoints.TryGetValue(endpointName, out var endpointDefinition))
            {
                Log.Warning("Endpoint not found: {EndpointName}", endpointName);
                return NotFound(new { error = $"Endpoint '{endpointName}' not found", success = false });
            }

            // Translate HTTP method if configured
            var originalMethod = method;
            var incomingMethod = Request?.Method ?? originalMethod;
            var translatedMethod = PortwayApi.Classes.Helpers.HttpMethodTranslator.TranslateMethod(incomingMethod, endpointDefinition.CustomProperties);
            originalMethod = incomingMethod;

            if (!incomingMethod.Equals(translatedMethod, StringComparison.OrdinalIgnoreCase))
            {
                Log.Debug("Parsed HTTP method translation: {OriginalMethod} -> {TranslatedMethod} for endpoint {EndpointName}",
                    incomingMethod, translatedMethod, endpointName);
            }
            // Check if method is allowed (using original method for validation)
            if (!endpointDefinition.Methods.Contains(originalMethod))
            {
                Log.Warning("Method {Method} not allowed for endpoint {EndpointName}", 
                    originalMethod, endpointName);
                return StatusCode(405);
            }

            // Validate translated method
            if (!PortwayApi.Classes.Helpers.HttpMethodTranslator.IsValidHttpMethod(translatedMethod))
            {
                Log.Warning("Translated method {TranslatedMethod} is not valid for endpoint {EndpointName}", 
                    translatedMethod, endpointName);
                return StatusCode(500, new { error = "Invalid translated HTTP method" });
            }

            // Convert endpoint definition to tuple format for legacy compatibility
            var endpointConfig = endpointDefinition.ToTuple();

            // Construct full URL
            var queryString = HttpContext?.Request?.QueryString.Value ?? string.Empty;
            var fullUrl = endpointConfig.Url;

            // Special handling for DELETE method based on DeletePattern
            if (method.Equals("DELETE", StringComparison.OrdinalIgnoreCase))
            {
                var delEncodedId = Uri.EscapeDataString(id ?? string.Empty);
                fullUrl = ConstructDeleteUrl(fullUrl, delEncodedId, remainingPath, endpointConfig, endpointName);
            }
            else
            {
                // Normalize base URL to avoid double slashes when appending
                fullUrl = fullUrl?.TrimEnd('/') ?? string.Empty;

                // Smart ID handling:
                if (!string.IsNullOrEmpty(id))
                {
                    // URL-encode id values (id is non-null here)
                    var encodedId = Uri.EscapeDataString(id!);

                    // If the base URL contains a {id} placeholder, replace it
                    if (fullUrl.Contains("{id}", StringComparison.OrdinalIgnoreCase))
                    {
                        fullUrl = fullUrl.Replace("{id}", encodedId, StringComparison.OrdinalIgnoreCase);
                        Log.Debug("Replaced {id} placeholder in URL: {Url} (ID: {Id})", fullUrl, id);
                    }
                    else if (Guid.TryParse(id, out _))
                    {
                        // Prefer OData GUID format if GUID detected and base looks OData-ish (heuristic)
                        fullUrl += $"(guid'{encodedId}')";
                        Log.Debug("Appended OData GUID to URL: {Url} (ID: {Id})", fullUrl, id);
                    }
                    else if (long.TryParse(id, out _))
                    {
                        // Numeric key as OData key
                        fullUrl += $"({encodedId})";
                        Log.Debug("Appended numeric key to URL: {Url} (ID: {Id})", fullUrl, id);
                    }
                    else
                    {
                        // Fallback: append as path segment
                        fullUrl += $"/{Uri.EscapeDataString(id!)}";
                        Log.Debug("Appended ID as path segment to URL: {Url} (ID: {Id})", fullUrl, id);
                    }
                }
                else if (!string.IsNullOrEmpty(remainingPath))
                {
                    // Normalize remainingPath and encode each segment to preserve slashes
                    var segments = remainingPath.Split('/', StringSplitOptions.RemoveEmptyEntries)
                                                .Select(s => Uri.EscapeDataString(s));
                    fullUrl += "/" + string.Join('/', segments);
                    Log.Debug("Added remaining path to URL: {Url} (Path: {RemainingPath})", fullUrl, remainingPath);
                }
                else
                {
                    Log.Debug("No ID or remaining path found for URL: {Url}", fullUrl);
                }
            }

            // Append query string safely (avoid double '?' and preserve existing query params)
            if (!string.IsNullOrEmpty(queryString))
            {
                // Request.QueryString.Value begins with '?' when non-empty
                var qs = queryString.StartsWith("?") ? queryString.Substring(1) : queryString;
                fullUrl += fullUrl.Contains('?') ? "&" + qs : "?" + qs;
            }

            Log.Debug("Final constructed URL: {Url}", fullUrl);

            // Store the target URL in the context items for logging
            if (HttpContext != null)
            {
                HttpContext.Items["TargetUrl"] = fullUrl;
            }

            // Validate URL safety
            if (!_urlValidator.IsUrlSafe(fullUrl))
            {
                Log.Warning("Blocked potentially unsafe URL: {Url}", fullUrl);
                return StatusCode(403);
            }

            // Detect if this is likely a SOAP request
            bool isSoapRequest = (Request?.ContentType?.Contains("text/xml") == true) ||
                                 (Request?.ContentType?.Contains("application/soap+xml") == true) ||
                                 (fullUrl.Contains(".svc") && !fullUrl.Contains("REST")) ||
                                 (Request?.Headers?.ContainsKey("SOAPAction") == true);

            if (isSoapRequest)
            {
                Log.Debug("Detected SOAP request for endpoint: {Endpoint}", endpointName);
                // SOAP requests generally shouldn't be cached, so bypass cache and execute directly
                await ExecuteProxyRequest(translatedMethod, fullUrl, env, endpointConfig, endpointName, isSoapRequest: true, originalMethod: originalMethod, endpointDefinition: endpointDefinition);
                return new EmptyResult(); // Response already written
            }

            // For GET requests, try to use cache
            if (originalMethod.Equals("GET", StringComparison.OrdinalIgnoreCase))
            {
                // Create a cache key based on the request details
                var headers = Request?.Headers ?? new Microsoft.AspNetCore.Http.HeaderDictionary();
                string cacheKey = CreateCacheKey(env, endpointName, remainingPath ?? string.Empty, queryString, headers);
                
                // Try to get from cache first
                var cacheEntry = await _cacheManager.GetAsync<Services.Caching.ProxyCacheEntry>(cacheKey);
                
                if (cacheEntry != null)
                {
                    Log.Debug("Cache hit for proxy request: {Endpoint}, URL: {Url}", endpointName, fullUrl);
                    
                    // Apply cached headers and status code
                    foreach (var header in cacheEntry.Headers)
                    {
                        Response.Headers[header.Key] = header.Value;
                    }
                    
                    Response.StatusCode = cacheEntry.StatusCode;
                    
                    // Write cached content
                    await Response.WriteAsync(cacheEntry.Content);
                    
                    return new EmptyResult(); // Response already written
                }
                
                Log.Debug("Cache miss for proxy request: {Endpoint}, URL: {Url}", endpointName, fullUrl);
                
                // Acquire a distributed lock to prevent duplicate requests
                using var lockHandle = await _cacheManager.AcquireLockAsync(
                    cacheKey, 
                    TimeSpan.FromSeconds(30),
                    TimeSpan.FromSeconds(10),
                    TimeSpan.FromMilliseconds(200));
                
                if (lockHandle != null)
                {
                    // Double-check cache after acquiring lock
                    cacheEntry = await _cacheManager.GetAsync<Services.Caching.ProxyCacheEntry>(cacheKey);
                    
                    if (cacheEntry != null)
                    {
                        Log.Debug("Cache hit after lock for proxy request: {Endpoint}", endpointName);
                        
                        // Apply cached headers and status code
                        foreach (var header in cacheEntry.Headers)
                        {
                            Response.Headers[header.Key] = header.Value;
                        }
                        
                        Response.StatusCode = cacheEntry.StatusCode;
                        
                        // Write cached content
                        await Response.WriteAsync(cacheEntry.Content);
                        
                        return new EmptyResult(); // Response already written
                    }
                    
                    // Continue with normal proxy process for cache miss
                    var responseDetails = await ExecuteProxyRequest(translatedMethod, fullUrl, env, endpointConfig, endpointName, originalMethod: originalMethod, endpointDefinition: endpointDefinition);
                    
                    // For successful responses, store in cache
                    if (responseDetails.IsSuccessful && _cacheManager.ShouldCacheResponse(responseDetails.ContentType))
                    {
                        // Determine cache duration - default to endpoint-specific duration
                        TimeSpan cacheDuration = _cacheManager.GetCacheDurationForEndpoint(endpointName);
                        
                        // Check for Cache-Control max-age directive
                        if (responseDetails.Headers.TryGetValue("Cache-Control", out var cacheControl))
                        {
                            var maxAgeMatch = Regex.Match(cacheControl, @"max-age=(\d+)");
                            if (maxAgeMatch.Success && int.TryParse(maxAgeMatch.Groups[1].Value, out int maxAge))
                            {
                                cacheDuration = TimeSpan.FromSeconds(maxAge);
                            }
                        }
                        
                        // Store response in cache
                        var entry = Services.Caching.ProxyCacheEntry.Create(
                            responseDetails.Content,
                            responseDetails.Headers,
                            responseDetails.StatusCode);
                        
                        await _cacheManager.SetAsync(cacheKey, entry, cacheDuration);
                        
                        Log.Debug("Cached proxy response for: {Endpoint} ({Duration} seconds)", 
                            endpointName, cacheDuration.TotalSeconds);
                    }
                }
                else
                {
                    // If we couldn't acquire a lock, just execute the request without caching
                    Log.Warning("⏱️ Could not acquire lock for caching: {Endpoint}", endpointName);
                    await ExecuteProxyRequest(translatedMethod, fullUrl, env, endpointConfig, endpointName, originalMethod: originalMethod, endpointDefinition: endpointDefinition);
                }
                
                return new EmptyResult(); // Response already written
            }
            else
            {
                // For non-GET requests, just execute the proxy request without caching
                await ExecuteProxyRequest(translatedMethod, fullUrl, env, endpointConfig, endpointName, originalMethod: originalMethod, endpointDefinition: endpointDefinition);
                return new EmptyResult(); // Response already written
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during proxy request: {EndpointName}", endpointName);

            return Problem(
                detail: $"Error processing endpoint {endpointName}: {ex.Message}",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Error"
            );
        }
    }

    /// <summary>
    /// Constructs the DELETE URL based on configured DeletePattern
    /// </summary>
    private string ConstructDeleteUrl(
        string baseUrl, 
        string? id, 
        string remainingPath, 
        (string Url, HashSet<string> Methods, bool IsPrivate, string Type) endpointConfig,
        string endpointName)
    {
        var deletePattern = GetDeletePatternForProxy(endpointName);
        var style = deletePattern?.Style ?? string.Empty;

        // Normalize baseUrl
        baseUrl = baseUrl?.TrimEnd('/') ?? string.Empty;

        // Known styles
        var knownStyles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "PathParameter", "QueryParameter", "ODataGuid", "ODataKey" };

        // Derive style if missing or unknown
        if (string.IsNullOrWhiteSpace(style) || !knownStyles.Contains(style))
        {
            if (baseUrl.IndexOf("{id}", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                style = "PathParameter";
            }
            else if (!string.IsNullOrEmpty(id) && Guid.TryParse(id, out _))
            {
                style = "ODataGuid";
            }
            else if (!string.IsNullOrEmpty(id) && long.TryParse(id, out _))
            {
                style = "ODataKey";
            }
            else if (!string.IsNullOrEmpty(remainingPath))
            {
                style = "PathParameter";
            }
            else
            {
                style = "PathParameter";
            }
        }

        // Safely encode id and remainingPath segments
        var encodedId = string.IsNullOrEmpty(id) ? string.Empty : Uri.EscapeDataString(id);

        switch (style)
        {
            case "PathParameter":
                if (!string.IsNullOrEmpty(encodedId) && baseUrl.IndexOf("{id}", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    baseUrl = Regex.Replace(baseUrl, @"\{id\}", encodedId, RegexOptions.IgnoreCase);
                    Log.Debug("DELETE using PathParameter (placeholder): {Url} (ID: {Id})", baseUrl, id);
                }
                else if (!string.IsNullOrEmpty(encodedId))
                {
                    baseUrl = $"{baseUrl}/{encodedId}";
                    Log.Debug("DELETE using PathParameter: {Url} (ID: {Id})", baseUrl, id);
                }
                else if (!string.IsNullOrEmpty(remainingPath))
                {
                    var segments = remainingPath.Split('/', StringSplitOptions.RemoveEmptyEntries)
                                               .Select(s => Uri.EscapeDataString(s));
                    baseUrl = $"{baseUrl}/{string.Join('/', segments)}";
                    Log.Debug("DELETE using PathParameter (remainingPath): {Url} (Path: {Path})", baseUrl, remainingPath);
                }
                break;

            case "QueryParameter":
                if (!string.IsNullOrEmpty(encodedId))
                {
                    baseUrl = baseUrl + (baseUrl.Contains('?') ? "&" : "?") + $"id={encodedId}";
                    Log.Debug("DELETE using QueryParameter: {Url} (ID: {Id})", baseUrl, id);
                }
                else
                {
                    Log.Debug("DELETE using QueryParameter but no ID provided, leaving base URL unchanged: {Url}", baseUrl);
                }
                break;

            case "ODataGuid":
                if (!string.IsNullOrEmpty(encodedId))
                {
                    baseUrl = $"{baseUrl}(guid'{encodedId}')";
                    Log.Debug("DELETE using ODataGuid: {Url} (ID: {Id})", baseUrl, id);
                }
                break;

            case "ODataKey":
                if (!string.IsNullOrEmpty(encodedId))
                {
                    baseUrl = $"{baseUrl}({encodedId})";
                    Log.Debug("DELETE using ODataKey: {Url} (ID: {Id})", baseUrl, id);
                }
                break;

            default:
                if (!string.IsNullOrEmpty(encodedId))
                {
                    baseUrl = $"{baseUrl}/{encodedId}";
                    Log.Debug("DELETE using fallback PathParameter: {Url} (ID: {Id})", baseUrl, id);
                }
                break;
        }

        return baseUrl;
    }

    /// <summary>
    /// Cache entry for proxy responses
    /// </summary>
    private class ProxyCacheEntry
    {
        public string Content { get; set; } = string.Empty;
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
        public int StatusCode { get; set; } = 200;
    }

    /// <summary>
    /// Creates a cache key based on request details
    /// </summary>
    private string CreateCacheKey(string env, string endpointName, string path, string queryString, IHeaderDictionary headers)
    {
        var keyBuilder = new StringBuilder();
        keyBuilder.Append($"proxy:{env}:{endpointName}:{path}:{queryString}");
        
        // Include authorization to differentiate between users if needed
        if (headers.TryGetValue("Authorization", out var authValues))
        {
            // Hash the token to avoid storing sensitive data in memory
            using var sha = SHA256.Create();
            var authBytes = Encoding.UTF8.GetBytes(authValues.ToString());
            var hashBytes = sha.ComputeHash(authBytes);
            var authHash = Convert.ToBase64String(hashBytes);
            
            keyBuilder.Append($":auth:{authHash}");
        }
        
        // Include other headers that might affect the response
        if (headers.TryGetValue("Accept-Language", out var langValues))
        {
            keyBuilder.Append($":lang:{langValues}");
        }
        
        return keyBuilder.ToString();
    }

    /// <summary>
    /// Executes the actual proxy request and writes the response
    /// </summary>
    private async Task<(bool IsSuccessful, string Content, Dictionary<string, string> Headers, int StatusCode, string? ContentType)> ExecuteProxyRequest(
        string method, string fullUrl, string env, 
        (string Url, HashSet<string> Methods, bool IsPrivate, string Type) endpointConfig,
        string endpointName,
        bool isSoapRequest = false,
        string? originalMethod = null,
        EndpointDefinition? endpointDefinition = null)
    {
        // Create HttpClient
        var client = _httpClientFactory.CreateClient("ProxyClient");

        // Create request message
        var requestMessage = new HttpRequestMessage(
            new HttpMethod(method), 
            fullUrl
        );

        // Log the actual HTTP method being sent to backend
        Log.Debug("Sending {Method} request to backend: {Url}", method, fullUrl);

        // Copy request body for methods that can have body content
        if (HttpMethods.IsPost(method) ||
            HttpMethods.IsPut(method) ||
            HttpMethods.IsPatch(method) ||
            HttpMethods.IsDelete(method) ||
            method.Equals("MERGE", StringComparison.OrdinalIgnoreCase))
        {
            // Enable buffering to allow multiple reads
            Request.EnableBuffering();
            
            // Read the request body
            var memoryStream = new MemoryStream();
            await Request.Body.CopyToAsync(memoryStream);
            
            // Reset position for potential downstream middleware
            memoryStream.Position = 0;
            Request.Body.Position = 0;
            
            // Set the request content
            requestMessage.Content = new StreamContent(memoryStream);
            
            // Copy content type header if present
            if (Request.ContentType != null)
            {
                requestMessage.Content.Headers.ContentType = 
                    new System.Net.Http.Headers.MediaTypeHeaderValue(Request.ContentType);
            }
        }

        // Copy headers
        foreach (var header in Request.Headers)
        {
            // Skip certain headers that shouldn't be proxied
            if (header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                header.Key.Equals("Connection", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                    continue; // Already handled for request content

                // Special handling for SOAP requests
                if (isSoapRequest && header.Key.Equals("SOAPAction", StringComparison.OrdinalIgnoreCase))
                {
                    // SOAPAction needs special handling - it must be enclosed in quotes for SOAP 1.1
                    string soapAction = header.Value.ToString();
                    if (!soapAction.StartsWith("\"") && !soapAction.EndsWith("\""))
                    {
                        soapAction = $"\"{soapAction}\"";
                    }
                    requestMessage.Headers.TryAddWithoutValidation("SOAPAction", soapAction);
                    continue;
                }

                requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not add header {HeaderKey}", header.Key);
            }
        }

        // Load environment settings
        var (_, _, envHeaders) = await _environmentSettingsProvider.LoadEnvironmentOrThrowAsync(env);

        // Add headers from environment settings
        foreach (var header in envHeaders)
        {
            try
            {
                requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value);
                Log.Debug("Added environment header: {HeaderKey}={HeaderValue}", header.Key, header.Value);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not add environment header {HeaderKey}", header.Key);
            }
        }

        // Add custom headers based on HttpMethodAppendHeaders custom property
        if (endpointDefinition != null && !string.IsNullOrEmpty(originalMethod))
        {
            // Get list of existing headers to check for conflicts
            var existingHeaders = new List<string>();
            
            // Collect headers from the request
            foreach (var header in Request.Headers)
            {
                existingHeaders.Add(header.Key);
            }
            
            // Collect headers from environment settings
            foreach (var header in envHeaders)
            {
                existingHeaders.Add(header.Key);
            }
            
            var customHeaders = PortwayApi.Classes.Helpers.HttpMethodHeaderAppender.GetAppendHeaders(
                originalMethod, method, endpointDefinition.CustomProperties, 
                existingHeaders, PortwayApi.Classes.Helpers.HeaderConflictResolution.Skip);
                
            foreach (var header in customHeaders)
            {
                try
                {
                    // Validate header name
                    if (PortwayApi.Classes.Helpers.HttpMethodHeaderAppender.IsValidHeaderName(header.Key))
                    {
                        // Check if header already exists in the request message
                        var headerExists = requestMessage.Headers.Contains(header.Key) || 
                                         (requestMessage.Content?.Headers.Contains(header.Key) == true);
                        
                        if (!headerExists)
                        {
                            requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value);
                            Log.Debug("Added custom header for method {OriginalMethod}: {HeaderKey}={HeaderValue}", 
                                originalMethod, header.Key, header.Value);
                        }
                        else
                        {
                            Log.Debug("Custom header {HeaderKey} already exists in request, skipping to avoid conflicts", header.Key);
                        }
                    }
                    else
                    {
                        Log.Warning("Invalid custom header name: {HeaderKey}", header.Key);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Could not add custom header {HeaderKey}", header.Key);
                }
            }
        }

        // Send the request
        var response = await client.SendAsync(requestMessage);
        
        // Store response headers for cache and apply to current response
        var responseHeaders = new Dictionary<string, string>();

        // Copy response headers
        foreach (var header in response.Headers)
        {
            if (!header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                Response.Headers[header.Key] = header.Value.ToArray();
                responseHeaders[header.Key] = string.Join(",", header.Value);
            }
        }

        // Copy content headers, but exclude Content-Length
        if (response.Content != null)
        {
            foreach (var header in response.Content.Headers)
            {
                if (!header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    Response.Headers[header.Key] = header.Value.ToArray();
                    responseHeaders[header.Key] = string.Join(",", header.Value);
                }
            }
        }
        
        // For GET requests, ensure Cache-Control header is set (except for SOAP)
        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase) && !isSoapRequest && !responseHeaders.ContainsKey("Cache-Control"))
        {
            // Add a default cache control header
            Response.Headers["Cache-Control"] = "public, max-age=300"; // 5 minutes
            responseHeaders["Cache-Control"] = "public, max-age=300";
        }

        // Set status code
        Response.StatusCode = (int)response.StatusCode;

        // Read and potentially rewrite response content
        var originalContent = response.Content != null
            ? await response.Content.ReadAsStringAsync()
            : string.Empty;

        // Extract content type
        string? contentType = null;
        if (response.Content?.Headers?.ContentType != null)
        {
            contentType = response.Content.Headers.ContentType.ToString();
        }

        // For SOAP responses, skip URL rewriting
        string rewrittenContent;
        if (isSoapRequest)
        {
            rewrittenContent = originalContent;
            
            // Ensure content type is correctly set for XML responses
            if (!Response.Headers.ContainsKey("Content-Type"))
            {
                if (originalContent.Contains("<soap:Envelope") || originalContent.Contains("<SOAP-ENV:Envelope"))
                {
                    Response.Headers["Content-Type"] = "text/xml; charset=utf-8";
                    responseHeaders["Content-Type"] = "text/xml; charset=utf-8";
                    contentType = "text/xml; charset=utf-8";
                }
            }
        }
        else
        {
            if (!Uri.TryCreate(endpointConfig.Url, UriKind.Absolute, out var originalUri))
            {
                Log.Warning("Could not parse endpoint URL as URI: {Url}", endpointConfig.Url);
                Response.StatusCode = 500;
                await Response.WriteAsync("Error processing request");
                return (false, string.Empty, responseHeaders, 500, null);
            }

            var originalHost = $"{originalUri.Scheme}://{originalUri.Host}:{originalUri.Port}";
            var originalPath = originalUri.AbsolutePath.TrimEnd('/');

            // Proxy path = /api/{env}/{endpoint}
            var proxyHost = $"{Request.Scheme}://{Request.Host}";
            var proxyPath = $"/api/{env}/{endpointName}";

            // Apply URL rewriting
            rewrittenContent = UrlRewriter.RewriteUrl(
                originalContent, 
                originalHost, 
                originalPath, 
                proxyHost, 
                proxyPath);
        }

        // Write the content to the response
        await Response.WriteAsync(rewrittenContent);

        Log.Debug("Proxy request completed: {Method} {Path} -> {StatusCode}", 
            method, Request.Path, response.StatusCode);
            
        return (
            response.IsSuccessStatusCode, 
            rewrittenContent, 
            responseHeaders, 
            (int)response.StatusCode,
            contentType
        );
    }

    /// <summary>
    /// Handles composite endpoint requests
    /// </summary>
    private async Task<IActionResult> HandleCompositeRequest(
        string env,
        string endpointName,
        string requestBody)
    {
        try
        {
            Log.Debug("Processing composite endpoint: {Endpoint}", endpointName);
            
            // Remove "composite/" prefix if present
            string compositeName = endpointName;
            if (endpointName.StartsWith("composite/", StringComparison.OrdinalIgnoreCase))
            {
                compositeName = endpointName.Substring("composite/".Length);
            }
            
            // Process the composite endpoint
            var result = await _compositeHandler.ProcessCompositeEndpointAsync(
                HttpContext, env, compositeName, requestBody);
                
            // Convert from IResult to IActionResult
            if (result is OkObjectResult okResult)
            {
                return Ok(okResult.Value);
            }
            else if (result is NotFoundObjectResult notFoundResult)
            {
                return NotFound(notFoundResult.Value);
            }
            else if (result is BadRequestObjectResult badRequestResult)
            {
                return BadRequest(badRequestResult.Value);
            }
            else if (result is ObjectResult objectResult)
            {
                return StatusCode(objectResult.StatusCode ?? 500, objectResult.Value);
            }
            else
            {
                // Default successful response
                return Ok(new
                {
                    success = true,
                    message = "Record(s) created successfully", 
                });
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during composite (proxy) request: {EndpointName}", endpointName);
            
            return Problem(
                detail: $"Error processing endpoint {endpointName}: {ex.Message}",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Error"
            );
        }
    }

    /// <summary>
    /// Handles webhook requests
    /// </summary>
    private async Task<IActionResult> HandleWebhookRequest(
        string env,
        string webhookEndpointKey,
        string webhookId,
        JsonElement payload)
    {
        var requestUrl = $"{Request.Scheme}://{Request.Host}{Request.Path}";
        Log.Debug("Webhook received: {Method} {Url}", Request.Method, requestUrl);

        try
        {
            // Validate environment and get connection string
            var (connectionString, serverName, _) = await _environmentSettingsProvider.LoadEnvironmentOrThrowAsync(env);

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return BadRequest(new { 
                    error = "Environment is not configured properly.", 
                    success = false 
                });
            }

            // Load webhook endpoint configuration using the namespace-aware key
            var webhookEndpoints = EndpointHandler.GetSqlWebhookEndpoints();
            if (!webhookEndpoints.TryGetValue(webhookEndpointKey, out var endpointConfig))
            {
                Log.Warning("Webhook endpoint not configured properly: {WebhookEndpoint}", webhookEndpointKey);

                return NotFound(new
                {
                    error = $"Endpoint '{webhookEndpointKey}' is not configured properly.",
                    success = false
                });
            
            }

            // Get table name and schema from the configuration
            var tableName = endpointConfig.DatabaseObjectName ?? "WebhookData";
            var schema = endpointConfig.DatabaseSchema ?? "dbo";

            // Validate webhook ID against allowed columns
            var allowedColumns = endpointConfig.AllowedColumns ?? new List<string>();
            if (allowedColumns.Any() && 
                !allowedColumns.Contains(webhookId, StringComparer.OrdinalIgnoreCase))
            {
                return NotFound(new { 
                    error = $"Webhook ID '{webhookId}' is not configured.", 
                    success = false 
                });
            }

            // Insert webhook data
            using var connection = new SqlConnection(_connectionPoolService.OptimizeConnectionString(connectionString));
            await connection.OpenAsync();

            var insertQuery = $@"
                INSERT INTO [{schema}].[{tableName}] (WebhookId, Payload, ReceivedAt)
                OUTPUT INSERTED.Id
                VALUES (@WebhookId, @Payload, @ReceivedAt)";

            var insertedId = await connection.ExecuteScalarAsync<int>(insertQuery, new
            {
                WebhookId = webhookId,
                Payload = payload.ToString(),
                ReceivedAt = DateTime.UtcNow
            });

            Log.Debug("Webhook processed successfully: {WebhookId} (ID: {InsertedId})", 
                webhookId, insertedId);

            return Ok(new
            {
                message = "Webhook processed successfully.",
                id = insertedId
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during webhook processing: {WebhookId}", webhookId);
            
            return Problem(
                detail: $"Error processing. Please check the logs for more details.",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Error"
            );
        }
    }

    /// <summary>
    /// Helper method to build next link for pagination
    /// </summary>
    private string BuildNextLink(
        string env, 
        string endpointPath, 
        string? select, 
        string? filter, 
        string? orderby, 
        int top, 
        int skip)
    {
        var nextLink = $"/api/{env}/{endpointPath}?$top={top}&$skip={skip + top}";

        if (!string.IsNullOrWhiteSpace(select))
            nextLink += $"&$select={Uri.EscapeDataString(select)}";
        
        if (!string.IsNullOrWhiteSpace(filter))
            nextLink += $"&$filter={Uri.EscapeDataString(filter)}";
        
        if (!string.IsNullOrWhiteSpace(orderby))
            nextLink += $"&$orderby={Uri.EscapeDataString(orderby)}";

        return nextLink;
    }

    /// <summary>
    /// Helper method to convert JsonElement to appropriate parameter value
    /// </summary>
    private static object? GetParameterValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt32(out int intValue) ? intValue 
                : element.TryGetDouble(out double doubleValue) ? doubleValue 
                : (object?)null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.ToString()
        };
    }

    /// <summary>
    /// Handles SQL GET requests
    /// </summary>
    private async Task<IActionResult> HandleSqlGetRequest(
        string env, 
        string endpointName,
        string? id,
        string? remainingPath,
        string? select, 
        string? filter,
        string? orderby,
        int top,
        int skip)
    {
        var url = $"{Request.Scheme}://{Request.Host}{Request.Path}{Request.QueryString}";
        Log.Debug("SQL Query Request: {Url}", url);

        try
        {
            // Check if this is a SQL endpoint - if not, return 404
            var sqlEndpoints = EndpointHandler.GetSqlEndpoints();
            if (!sqlEndpoints.ContainsKey(endpointName))
            {
                return NotFound();
            }

            // Step 1: Validate environment
            var (connectionString, serverName, _) = await _environmentSettingsProvider.LoadEnvironmentOrThrowAsync(env);
            if (string.IsNullOrEmpty(connectionString))
            {
                return BadRequest(new { 
                    error = $"Invalid or missing environment: {env}", 
                    success = false 
                });
            }

            // Step 2: Get endpoint configuration 
            var endpoint = sqlEndpoints[endpointName];

            // Step 2.1: Apply endpoint-specific property overrides
            top = ApplyMaxPageSizeLimit(top, endpoint);
            orderby = ApplyDefaultSorting(orderby, endpoint);

            // Check if this is a Table Valued Function endpoint
            if (PortwayApi.Classes.Helpers.TableValuedFunctionHelper.IsTableValuedFunction(endpoint))
            {
                Log.Debug("Detected Table Valued Function endpoint: {FunctionName}", endpoint.DatabaseObjectName);
                
                // Extract path segments for parameter values
                var pathSegments = string.IsNullOrEmpty(remainingPath) 
                    ? new string[0] 
                    : remainingPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

                // Prepare OData parameters for TVF handling
                var tvfODataParams = new Dictionary<string, string>
                {
                    { "top", (top + 1).ToString() }, // +1 for pagination detection
                    { "skip", skip.ToString() }
                };

                if (!string.IsNullOrEmpty(select)) 
                    tvfODataParams["select"] = select;
                if (!string.IsNullOrEmpty(filter)) 
                    tvfODataParams["filter"] = filter;
                if (!string.IsNullOrEmpty(orderby)) 
                    tvfODataParams["orderby"] = orderby;

                // Handle TVF request using the dedicated handler
                var tvfResult = await PortwayApi.Classes.Handlers.TableValuedFunctionSqlHandler.HandleTVFGetRequest(
                    endpoint,
                    Request,
                    pathSegments,
                    _connectionPoolService.OptimizeConnectionString(connectionString),
                    tvfODataParams);

                bool tvfSuccess = tvfResult.Item1;
                IActionResult? tvfActionResult = tvfResult.Item2;
                List<object>? tvfData = tvfResult.Item3;

                if (!tvfSuccess)
                {
                    return tvfActionResult!;
                }

                // Process results for pagination and response formatting
                var tvfResultList = tvfData!;

                // Determine if it's the last page
                bool tvfIsLastPage = tvfResultList.Count <= top;
                if (!tvfIsLastPage)
                {
                    // Remove the extra row used for pagination
                    tvfResultList.RemoveAt(tvfResultList.Count - 1);
                }

                // For ID-based requests (if applicable to TVF), return single item
                if (!string.IsNullOrEmpty(id))
                {
                    if (tvfResultList.Count == 0)
                    {
                        return NotFound(new {
                            error = $"No record found with specified parameters",
                            success = false
                        });
                    }
                    
                    return Ok(tvfResultList.FirstOrDefault());
                }

                // Return collection response
                var tvfResponse = new
                {
                    Count = tvfResultList.Count,
                    Value = tvfResultList,
                    NextLink = tvfIsLastPage 
                        ? null 
                        : BuildNextLink(env, endpointName, select, filter, orderby, top, skip)
                };

                Log.Debug("Successfully processed TVF query for {FunctionName}", endpoint.DatabaseObjectName);
                return Ok(tvfResponse);
            }

            // Step 3: Extract endpoint details
            var schema = endpoint.DatabaseSchema ?? "dbo";
            var objectName = endpoint.DatabaseObjectName;
            var allowedColumns = endpoint.AllowedColumns ?? new List<string>();
            var allowedMethods = endpoint.Methods ?? new List<string> { "GET" };
            var primaryKey = endpoint.PrimaryKey ?? "Id";

            // Check if GET is allowed
            if (!allowedMethods.Contains("GET"))
            {
                return StatusCode(405);
            }

            // Step 4: Handle ID-based filtering
            if (!string.IsNullOrEmpty(id))
            {
                // Get the actual database column name for the primary key
                // The primaryKey could be an alias, so we need to resolve it to the database column name
                string actualPrimaryKey = primaryKey;
                
                if (allowedColumns.Count > 0)
                {
                    var aliasToDatabase = endpoint.AliasToDatabase;
                    if (aliasToDatabase.TryGetValue(primaryKey, out var databasePrimaryKey))
                    {
                        actualPrimaryKey = databasePrimaryKey;
                        Log.Debug("Converted primary key alias '{Alias}' to database column '{DatabaseColumn}'", primaryKey, actualPrimaryKey);
                    }
                }
                
                // Create appropriate filter expression by primary key
                // Check if the ID is a GUID
                if (Guid.TryParse(id, out _))
                {
                    filter = $"{actualPrimaryKey} eq guid'{id}'";
                }
                else
                {
                    // Handle numeric or string IDs
                    bool isNumeric = long.TryParse(id, out _);
                    filter = isNumeric 
                        ? $"{actualPrimaryKey} eq {id}" 
                        : $"{actualPrimaryKey} eq '{id}'";
                }

                // Set top to 1 to return only one record when requesting by ID
                top = 1;
                
                Log.Debug("Created filter for ID-based query: {Filter}", filter);
            }

            // Step 5: Handle column aliases and validation
            string? selectForQuery = select; // This will contain database column names for the SQL query
            string? filterForQuery = filter; // This will contain database column names for the SQL query
            string? orderbyForQuery = orderby; // This will contain database column names for the SQL query
            
            if (allowedColumns.Count > 0)
            {
                // Get column mappings for alias support
                var aliasToDatabase = endpoint.AliasToDatabase;
                var databaseToAlias = endpoint.DatabaseToAlias;
                
                // Validate select columns (using aliases)
                if (!string.IsNullOrEmpty(select))
                {
                    var (isValid, invalidAliases) = PortwayApi.Classes.Helpers.ColumnMappingHelper.ValidateAliasColumns(select, aliasToDatabase);
                    
                    if (!isValid)
                    {
                        return BadRequest(new { 
                            error = $"Selected columns not allowed: {string.Join(", ", invalidAliases)}", 
                            success = false 
                        });
                    }
                    
                    // Convert aliases to database column names for the SQL query
                    selectForQuery = PortwayApi.Classes.Helpers.ColumnMappingHelper.ConvertAliasesToDatabaseColumns(select, aliasToDatabase);
                    Log.Debug("Converted aliases '{Aliases}' to database columns '{DatabaseColumns}'", select, selectForQuery);
                }
                else
                {
                    // If no select and columns are restricted, use all allowed database columns
                    var allDatabaseColumns = PortwayApi.Classes.Helpers.ColumnMappingHelper.GetDatabaseColumns(databaseToAlias);
                    selectForQuery = string.Join(",", allDatabaseColumns);
                    Log.Debug("No select specified, using all allowed database columns: {DatabaseColumns}", selectForQuery);
                }
                
                // Convert filter column references from aliases to database columns
                if (!string.IsNullOrEmpty(filter))
                {
                    filterForQuery = PortwayApi.Classes.Helpers.ColumnMappingHelper.ConvertODataFilterAliases(filter, aliasToDatabase);
                    if (filterForQuery != filter)
                    {
                        Log.Debug("Converted filter aliases: '{OriginalFilter}' -> '{ConvertedFilter}'", filter, filterForQuery);
                    }
                }
                
                // Convert orderby column references from aliases to database columns
                if (!string.IsNullOrEmpty(orderby))
                {
                    orderbyForQuery = PortwayApi.Classes.Helpers.ColumnMappingHelper.ConvertODataOrderByAliases(orderby, aliasToDatabase);
                    if (orderbyForQuery != orderby)
                    {
                        Log.Debug("Converted orderby aliases: '{OriginalOrderBy}' -> '{ConvertedOrderBy}'", orderby, orderbyForQuery);
                    }
                }
            }

            // Step 6: Prepare OData parameters (using database column names)
            var odataParams = new Dictionary<string, string>
            {
                { "top", (top + 1).ToString() },
                { "skip", skip.ToString() }
            };

            if (!string.IsNullOrEmpty(selectForQuery)) 
                odataParams["select"] = selectForQuery;
            if (!string.IsNullOrEmpty(filterForQuery)) 
                odataParams["filter"] = filterForQuery;
            if (!string.IsNullOrEmpty(orderbyForQuery)) 
                odataParams["orderby"] = orderbyForQuery;

            // Step 7: Convert OData to SQL
            var (query, parameters) = _oDataToSqlConverter.ConvertToSQL($"{schema}.{objectName}", odataParams);

            // Step 8: Check cache first if enabled
            object? cachedResponse = null;
            string? cacheKey = null;
            bool cacheEnabled = IsCacheEnabled(endpoint);
            
            if (cacheEnabled)
            {
                // Create cache key based on query parameters
                cacheKey = $"sql:{env}:{endpointName}:{query.GetHashCode()}:{string.Join(",", parameters?.Select(p => $"{p.Key}={p.Value}") ?? new string[0])}";
                cachedResponse = await _cacheManager.GetAsync<object>(cacheKey);
                
                if (cachedResponse != null)
                {
                    Log.Debug("Cache hit for SQL query: {Endpoint}", endpointName);
                    return Ok(cachedResponse);
                }
            }

            // Step 9: Execute query
            await using var connection = new SqlConnection(_connectionPoolService.OptimizeConnectionString(connectionString));
            await connection.OpenAsync();

            var results = await connection.QueryAsync(query, parameters);
            var resultList = results.ToList();

            // Step 9: Transform results to use aliases (if column mappings exist)
            var transformedResults = resultList;
            if (allowedColumns.Count > 0)
            {
                var databaseToAlias = endpoint.DatabaseToAlias;
                if (databaseToAlias.Count > 0)
                {
                    var aliasResults = PortwayApi.Classes.Helpers.ColumnMappingHelper.TransformQueryResultsToAliases(resultList, databaseToAlias);
                    transformedResults = aliasResults.Cast<object>().ToList();
                    Log.Debug("Transformed {Count} results from database columns to aliases", transformedResults.Count);
                }
            }

            // Determine if it's the last page
            bool isLastPage = transformedResults.Count <= top;
            if (!isLastPage)
            {
                // Remove the extra row used for pagination
                transformedResults.RemoveAt(transformedResults.Count - 1);
            }

            // For ID-based requests, return the single item directly
            if (!string.IsNullOrEmpty(id))
            {
                // Return 404 if no results found
                if (transformedResults.Count == 0)
                {
                    return NotFound(new {
                        error = $"No record found with {primaryKey} = {id}",
                        success = false
                    });
                }
                
                // Return the single item directly (without wrapping in a collection)
                var singleItemResponse = transformedResults.FirstOrDefault();
                
                // Cache the single item response if caching is enabled
                if (cacheEnabled && !string.IsNullOrEmpty(cacheKey))
                {
                    var cacheDuration = GetCacheDurationMinutes(endpoint);
                    await _cacheManager.SetAsync(cacheKey, singleItemResponse, TimeSpan.FromMinutes(cacheDuration));
                    Log.Debug("Cached SQL single item response for {Endpoint}, duration: {Duration} minutes", endpointName, cacheDuration);
                }
                
                return Ok(singleItemResponse);
            }

            // Step 10: Prepare response for collection requests
            var response = new
            {
                Count = transformedResults.Count,
                Value = transformedResults,
                NextLink = isLastPage 
                    ? null 
                    : BuildNextLink(env, endpointName, select, filter, orderby, top, skip)
            };

            Log.Debug("Successfully processed query for {Endpoint}", endpointName);
            
            // Cache the response if caching is enabled
            if (cacheEnabled && !string.IsNullOrEmpty(cacheKey))
            {
                var cacheDuration = GetCacheDurationMinutes(endpoint);
                await _cacheManager.SetAsync(cacheKey, response, TimeSpan.FromMinutes(cacheDuration));
                Log.Debug("Cached SQL response for {Endpoint}, duration: {Duration} minutes", endpointName, cacheDuration);
            }
            
            return Ok(response);
        }
        catch (SqlException sqlEx)
        {
            // Handle SQL-specific exceptions with more detail
            // Generate a masked error reference for troubleshooting
            var errorId = Guid.NewGuid().ToString("N").Substring(0, 8);
            Log.Error(sqlEx, "SQL Error [{ErrorId}] during query for endpoint: {EndpointName}. SQL Error Number: {ErrorNumber}, Severity: {Severity}, State: {State}, Message: {Message}",
                errorId, endpointName, sqlEx.Number, sqlEx.Class, sqlEx.State, sqlEx.Message);

            // Provide only generic error messages for all SQL errors to avoid leaking database details
            string errorMessage = sqlEx.Number switch
            {
                2 => $"A data error occurred. Please contact support with reference: T00{errorId}", // Connection error
                53 => $"A data error occurred. Please contact support with reference: T00{errorId}", // Network error
                208 => $"A data error occurred. Please contact support with reference: T0{errorId}", // Invalid object name
                547 => $"A data error occurred. Please contact support with reference: T{errorId}", // Constraint violation
                1205 => $"A data error occurred. Please contact support with reference: T{errorId}", // Deadlock
                2627 => $"A data error occurred. Please contact support with reference: T{errorId}", // Unique constraint
                2601 => $"A data error occurred. Please contact support with reference: T{errorId}", // Duplicate key
                4060 => $"A data error occurred. Please contact support with reference: T{errorId}", // Cannot open database
                8152 => $"A data error occurred. Please contact support with reference: T{errorId}", // Data too long
                8114 => $"A data error occurred. Please contact support with reference: T{errorId}", // Data conversion
                18456 => $"A data error occurred. Please contact support with reference: T{errorId}", // Login failed
                50000 => $"A data error occurred. Please contact support with reference: T{errorId}", // User-defined error
                _ => $"An error occurred while processing your request. Reference: H{errorId}"
            };

            return Problem(
                detail: errorMessage,
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Error"
            );
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during SQL query for endpoint: {EndpointName}. Exception Type: {ExceptionType}", 
                endpointName, ex.GetType().Name);
            
            return Problem(
                detail: $"Error processing. Please check the logs for more details.",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Error"
            );
        }
    }

	/// <summary>
	/// Handles SQL POST requests (Create)
	/// </summary>
	private async Task<IActionResult> HandleSqlPostRequest(
		string env,
		string endpointName,
		JsonElement data)
	{
		try
		{
			// Check if this is a SQL endpoint
			var sqlEndpoints = EndpointHandler.GetSqlEndpoints();
			if (!sqlEndpoints.ContainsKey(endpointName))
			{
				return NotFound(new { error = $"Endpoint '{endpointName}' not found", success = false });
			}

			// Validate environment
			var (connectionString, serverName, _) = await _environmentSettingsProvider.LoadEnvironmentOrThrowAsync(env);
			if (string.IsNullOrEmpty(connectionString))
			{
				return CreateErrorResponse($"Invalid or missing environment: {env}");
			}

			// Get endpoint configuration
			var endpoint = sqlEndpoints[endpointName];

			// Check method support and procedure definition
			if (!(endpoint.Methods?.Contains("POST") ?? false))
			{
				return CreateErrorResponse("This endpoint does not support POST operations", null, StatusCodes.Status405MethodNotAllowed);
			}

			if (string.IsNullOrEmpty(endpoint.Procedure))
			{
				return CreateErrorResponse("This endpoint does not support insert operations");
			}

			// Validate input data against allowed columns, required columns, and regex patterns
			var (isValid, errorMessage, validationErrors) = ValidateSqlInput(data, endpoint, "POST");
			if (!isValid)
			{
				if (validationErrors != null && validationErrors.Any())
				{
					return StatusCode(422, new
					{
						error = "Validation failed",
						details = validationErrors.Select(e => new
						{
							field = e.Field,
							message = e.Message
						})
					});
				}
				return BadRequest(new { error = errorMessage, success = false });
			}

			// Prepare stored procedure parameters
			var dynamicParams = new DynamicParameters();
			dynamicParams.Add("@Method", "INSERT");
			
			if (User.Identity?.Name != null)
			{
				dynamicParams.Add("@UserName", User.Identity.Name);
			}

			// Extract and add parameters
			foreach (var property in data.EnumerateObject())
			{
				dynamicParams.Add($"@{property.Name}", GetParameterValue(property.Value));
			}

			// Execute stored procedure
			await using var connection = new SqlConnection(_connectionPoolService.OptimizeConnectionString(connectionString));
			await connection.OpenAsync();
			
			// Parse procedure name
			string schema = "dbo";
			string procedureName = endpoint.Procedure;
			
			if (endpoint.Procedure.Contains("."))
			{
				var parts = endpoint.Procedure.Split('.');
				schema = parts[0].Trim('[', ']');
				procedureName = parts[1].Trim('[', ']');
			}

			// Special handling for SqlException to catch RAISERROR messages
			try
			{
				var result = await connection.QueryAsync(
					$"[{schema}].[{procedureName}]", 
					dynamicParams, 
					commandType: CommandType.StoredProcedure
				);

				var resultList = result.ToList();
				
				Log.Debug("Successfully executed INSERT procedure for {Endpoint}", endpointName);
				
				return Ok(new { 
					success = true,
					message = "Record created successfully", 
					result = resultList.FirstOrDefault() 
				});
			}
            catch (SqlException sqlEx) when (IsIntentionalUserError(sqlEx))
            {
                // Extract the custom error message from RAISERROR
                Log.Warning("Custom SQL error (RAISERROR) for {Endpoint}: {ErrorMessage}", endpointName, sqlEx.Message);
                
                return BadRequest(new
                {
                    success = false,
                    error = "Bad request",
                    details = sqlEx.Message
                });
            }
		}
		catch (SqlException sqlEx)
		{
			// Handle SQL-specific exceptions with more detail
			// Generate a masked error reference for troubleshooting
			var errorId = Guid.NewGuid().ToString("N").Substring(0, 8);
			Log.Error(sqlEx, "SQL Error [{ErrorId}] during query for endpoint: {EndpointName}. SQL Error Number: {ErrorNumber}, Severity: {Severity}, State: {State}, Message: {Message}",
				errorId, endpointName, sqlEx.Number, sqlEx.Class, sqlEx.State, sqlEx.Message);

			// Provide only generic error messages for all SQL errors to avoid leaking database details
			string errorMessage = sqlEx.Number switch
			{
				2 => $"A data error occurred. Please contact support with reference: T00{errorId}", // Connection error
				53 => $"A data error occurred. Please contact support with reference: T00{errorId}", // Network error
				208 => $"A data error occurred. Please contact support with reference: T0{errorId}", // Invalid object name
				547 => $"A data error occurred. Please contact support with reference: T{errorId}", // Constraint violation
				1205 => $"A data error occurred. Please contact support with reference: T{errorId}", // Deadlock
				2627 => $"A data error occurred. Please contact support with reference: T{errorId}", // Unique constraint
				2601 => $"A data error occurred. Please contact support with reference: T{errorId}", // Duplicate key
				4060 => $"A data error occurred. Please contact support with reference: T{errorId}", // Cannot open database
				8152 => $"A data error occurred. Please contact support with reference: T{errorId}", // Data too long
				8114 => $"A data error occurred. Please contact support with reference: T{errorId}", // Data conversion
				18456 => $"A data error occurred. Please contact support with reference: T{errorId}", // Login failed
				50000 => $"A data error occurred. Please contact support with reference: T{errorId}", // User-defined error
				_ => $"An error occurred while processing your request. Reference: H{errorId}"
			};

			return Problem(
				detail: errorMessage,
				statusCode: StatusCodes.Status500InternalServerError,
				title: "Internal Error"
			);
		}
		catch (Exception ex)
		{
			string errorMessage = "An error occurred while processing your request";
			string? errorDetail = null;
			
			if (ex is JsonException)
			{
				errorMessage = "Invalid JSON format in request";
				errorDetail = "The request body contains malformed JSON";
				return CreateErrorResponse(errorMessage, errorDetail, StatusCodes.Status400BadRequest);
			}
			
			Log.Error(ex, "Error processing request for endpoint {EndpointName}: {ErrorType}: {ErrorMessage}", 
				endpointName, ex.GetType().Name, ex.Message);
			
			return CreateErrorResponse(errorMessage, null, StatusCodes.Status500InternalServerError);
		}
	}

 /// <summary>
/// Determines if a SQL exception is an intentional user-facing error vs a system error
/// </summary>
private bool IsIntentionalUserError(SqlException sqlEx)
{
    // Only error 50000 is the default for intentional RAISERROR without specific error number
    // Other error numbers in 50000+ range could be system or custom errors
    return sqlEx.Number == 50000;
}

    /// <summary>
    /// Handles SQL PUT requests (Update)
    /// </summary>
    private async Task<IActionResult> HandleSqlPutRequest(
        string env,
        string endpointName,
        JsonElement data)
    {
        try
        {
            // Check if this is a SQL endpoint - if not, return 404
            var sqlEndpoints = EndpointHandler.GetSqlEndpoints();
            if (!sqlEndpoints.ContainsKey(endpointName))
            {
                return NotFound();
            }

            // Step 1: Validate environment
            var (connectionString, serverName, _) = await _environmentSettingsProvider.LoadEnvironmentOrThrowAsync(env);
            if (string.IsNullOrEmpty(connectionString))
            {
                return BadRequest(new
                {
                    error = $"Invalid or missing environment: {env}",
                    success = false
                });
            }

            // Step 2: Get endpoint configuration
            var endpoint = sqlEndpoints[endpointName];

            // Step 3: Check if the endpoint supports PUT and has a procedure defined
            if (!(endpoint.Methods?.Contains("PUT") ?? false))
            {
                return StatusCode(405, new
                {
                    error = "Method not allowed",
                    success = false
                });
            }

            if (string.IsNullOrEmpty(endpoint.Procedure))
            {
                return BadRequest(new
                {
                    error = "This endpoint does not support update operations",
                    success = false
                });
            }

            // Step 4: Validate input data against allowed columns, required columns, and regex patterns
            var (isValid, errorMessage, validationErrors) = ValidateSqlInput(data, endpoint, "PUT");
            if (!isValid)
            {
                if (validationErrors != null && validationErrors.Any())
                {
                    return StatusCode(422, new
                    {
                        error = "Validation failed",
                        details = validationErrors.Select(e => new
                        {
                            field = e.Field,
                            message = e.Message
                        })
                    });
                }
                return BadRequest(new { error = errorMessage, success = false });
            }

            // Step 5: Validate that ID is present for update operations
            var (isParamsValid, paramsErrorMessage) = ValidateSqlParameters(data, "UPDATE");
            if (!isParamsValid)
            {
                return BadRequest(new
                {
                    error = paramsErrorMessage,
                    success = false
                });
            }

            // Step 6: Prepare stored procedure parameters
            var dynamicParams = new DynamicParameters();

            // Add method parameter (always needed for the standard procedure pattern)
            dynamicParams.Add("@Method", "UPDATE");

            // Add user parameter if available
            if (User.Identity?.Name != null)
            {
                dynamicParams.Add("@UserName", User.Identity.Name);
            }

            // Step 7: Extract and add data parameters from the request
            foreach (var property in data.EnumerateObject())
            {
                dynamicParams.Add($"@{property.Name}", GetParameterValue(property.Value));
            }

            // Step 8: Execute stored procedure
            await using var connection = new SqlConnection(_connectionPoolService.OptimizeConnectionString(connectionString));
            await connection.OpenAsync();

            // Parse procedure name properly
            string schema = "dbo";
            string procedureName = endpoint.Procedure;

            if (endpoint.Procedure.Contains("."))
            {
                var parts = endpoint.Procedure.Split('.');
                schema = parts[0].Trim('[', ']');
                procedureName = parts[1].Trim('[', ']');
            }

            // Special handling for SqlException to catch RAISERROR messages
            try
            {
                var result = await connection.QueryAsync(
                    $"[{schema}].[{procedureName}]",
                    dynamicParams,
                    commandType: CommandType.StoredProcedure
                );

                // Convert result to a list (could be empty if no rows returned)
                var resultList = result.ToList();

                Log.Debug("Successfully executed UPDATE procedure for {Endpoint}", endpointName);

                // Return the results, which typically includes the updated record
                return Ok(new
                {
                    success = true,
                    message = "Record updated successfully",
                    result = resultList.FirstOrDefault()
                });
            }
            catch (SqlException sqlEx) when (IsIntentionalUserError(sqlEx))
            {
                // Extract the custom error message from RAISERROR
                Log.Warning("Custom SQL error (RAISERROR) for {Endpoint}: {ErrorMessage}", endpointName, sqlEx.Message);

                return BadRequest(new
                {
                    success = false,
                    error = "Bad request",
                    details = sqlEx.Message
                });
            }
        }
        catch (SqlException sqlEx)
        {
            // Handle other SQL exceptions
            Log.Error(sqlEx, "SQL Exception for {Endpoint}: {ErrorCode}, {ErrorMessage}",
                endpointName, sqlEx.Number, sqlEx.Message);

            return CreateErrorResponse("Internal operation failed", null, StatusCodes.Status500InternalServerError);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error processing UPDATE for {Endpoint}", endpointName);
            return CreateErrorResponse("An error occurred while processing your request", null, StatusCodes.Status500InternalServerError);
        }
    }

	/// <summary>
	/// Handles SQL PATCH requests (partial updates)
	/// </summary>
	private async Task<IActionResult> HandleSqlPatchRequest(
		string env,
		string endpointName,
		JsonDocument requestBody)
	{
		try
		{
			// Step 1: Check if this is a SQL endpoint
			var sqlEndpoints = EndpointHandler.GetSqlEndpoints();
			if (!sqlEndpoints.ContainsKey(endpointName))
			{
				return NotFound();
			}

			// Step 2: Validate environment
			var (connectionString, serverName, _) = await _environmentSettingsProvider.LoadEnvironmentOrThrowAsync(env);
			if (string.IsNullOrEmpty(connectionString))
			{
				return BadRequest(new
				{
					error = $"Invalid or missing environment: {env}",
					success = false
				});
			}

			// Step 3: Get endpoint configuration and check if PATCH is allowed
			var endpoint = sqlEndpoints[endpointName];

			if (!(endpoint.Methods?.Contains("PATCH") ?? false))
			{
				return StatusCode(405, new
				{
					error = "Method not allowed",
					success = false
				});
			}

			if (string.IsNullOrEmpty(endpoint.Procedure))
			{
				return BadRequest(new
				{
					error = "This endpoint does not support partial update operations",
					success = false
				});
			}

			// Step 4: Parse and validate request body
			var data = requestBody.RootElement;

			// Validate against allowed columns, required columns, and regex patterns
			var (isValid, errorMessage, validationErrors) = ValidateSqlInput(data, endpoint, "PATCH");
			if (!isValid)
			{
				if (validationErrors != null && validationErrors.Any())
				{
					return StatusCode(422, new
					{
						error = "Validation failed",
						details = validationErrors.Select(e => new
						{
							field = e.Field,
							message = e.Message
						})
					});
				}
				return BadRequest(new { error = errorMessage, success = false });
			}

			// Step 5: Validate that required parameters are present (especially ID)
			var (isParamsValid, paramsErrorMessage) = ValidateSqlParameters(data, "UPDATE");
			if (!isParamsValid)
			{
				return BadRequest(new
				{
					error = paramsErrorMessage,
					success = false
				});
			}

			// Step 6: Prepare stored procedure parameters
			var dynamicParams = new DynamicParameters();

			// Add method parameter - use "PATCH" to differentiate from full UPDATE
			dynamicParams.Add("@Method", "PATCH");

			// Add user parameter if available
			if (User.Identity?.Name != null)
			{
				dynamicParams.Add("@UserName", User.Identity.Name);
			}

			// Step 7: Extract and add data parameters from the request. Only the fields provided in the request will be updated
			foreach (var property in data.EnumerateObject())
			{
				dynamicParams.Add($"@{property.Name}", GetParameterValue(property.Value));
			}

			// Step 8: Execute stored procedure
			await using var connection = new SqlConnection(_connectionPoolService.OptimizeConnectionString(connectionString));
			await connection.OpenAsync();

			// Parse procedure name properly
			string schema = "dbo";
			string procedureName = endpoint.Procedure;

			if (endpoint.Procedure.Contains("."))
			{
				var parts = endpoint.Procedure.Split('.');
				schema = parts[0].Trim('[', ']');
				procedureName = parts[1].Trim('[', ']');
			}

			// Special handling for SqlException to catch RAISERROR messages
			try
			{
				var result = await connection.QueryAsync(
					$"[{schema}].[{procedureName}]",
					dynamicParams,
					commandType: CommandType.StoredProcedure
				);

				// Convert result to a list
				var resultList = result.ToList();

				Log.Debug("Successfully executed PATCH procedure for {Endpoint}", endpointName);

				// Return the results
				return Ok(new
				{
					success = true,
					message = "Record partially updated successfully",
					result = resultList.FirstOrDefault()
				});
			}
            catch (SqlException sqlEx) when (IsIntentionalUserError(sqlEx))			
            {
				// Extract the custom error message from RAISERROR
				Log.Warning("Custom SQL error (RAISERROR) for {Endpoint}: {ErrorMessage}", endpointName, sqlEx.Message);
				
                return BadRequest(new
                {
                    success = false,
                    error = "Bad request",
                    details = sqlEx.Message
                });
			}
		}
		catch (SqlException sqlEx)
		{
			// Handle other SQL exceptions
			Log.Error(sqlEx, "SQL Exception for {Endpoint}: {ErrorCode}, {ErrorMessage}", 
				endpointName, sqlEx.Number, sqlEx.Message);

			return CreateErrorResponse("Internal operation failed", null, StatusCodes.Status500InternalServerError);
		}
		catch (Exception ex)
		{
			Log.Error(ex, "Error processing PATCH for {Endpoint}", endpointName);
			return CreateErrorResponse("An error occurred while processing your request", null, StatusCodes.Status500InternalServerError);
		}
	}

    /// <summary>
    /// Handles SQL DELETE requests
    /// </summary>
    private async Task<IActionResult> HandleSqlDeleteRequest(
        string env,
        string endpointName,
        string id)
    {
        try
        {
            // Check if this is a SQL endpoint - if not, return 404
            var sqlEndpoints = EndpointHandler.GetSqlEndpoints();
            if (!sqlEndpoints.ContainsKey(endpointName))
            {
                return NotFound();
            }

            // Validate environment
            var (connectionString, serverName, _) = await _environmentSettingsProvider.LoadEnvironmentOrThrowAsync(env);
            if (string.IsNullOrEmpty(connectionString))
            {
                return BadRequest(new
                {
                    success = false,
                    error = $"Invalid or missing environment: {env}"                    
                });
            }

            // Get endpoint configuration
            var endpoint = sqlEndpoints[endpointName];

            // Check if the endpoint supports DELETE and has a procedure defined
            if (!(endpoint.Methods?.Contains("DELETE") ?? false))
            {
                return StatusCode(405, new
                {
                    error = "Method not allowed",
                    success = false
                });
            }

            if (string.IsNullOrEmpty(endpoint.Procedure))
            {
                return BadRequest(new
                {
                    success = false,
                    error = "This endpoint does not support delete operations"
                });
            }

            // Check if the ID is provided
            if (string.IsNullOrEmpty(id))
            {
                return BadRequest(new
                {
                    success = false,
                    error = "ID parameter is required for delete operations"
                });
            }

            // Prepare stored procedure parameters
            var dynamicParams = new DynamicParameters();

            // Add method parameter (always needed for the standard procedure pattern)
            dynamicParams.Add("@Method", "DELETE");

            // Handle different primary key parameter names
            var primaryKey = endpoint.PrimaryKey ?? "Id";
            dynamicParams.Add($"@{primaryKey}", id);

            // For backward compatibility, also add @id parameter
            if (!primaryKey.Equals("id", StringComparison.OrdinalIgnoreCase))
            {
                dynamicParams.Add("@id", id);
            }

            // Add user parameter if available
            if (User.Identity?.Name != null)
            {
                dynamicParams.Add("@UserName", User.Identity.Name);
            }

            // Execute stored procedure
            await using var connection = new SqlConnection(_connectionPoolService.OptimizeConnectionString(connectionString));
            await connection.OpenAsync();

            // Parse procedure name properly
            string schema = "dbo";
            string procedureName = endpoint.Procedure;

            if (endpoint.Procedure.Contains("."))
            {
                var parts = endpoint.Procedure.Split('.');
                schema = parts[0].Trim('[', ']');
                procedureName = parts[1].Trim('[', ']');
            }

            // Special handling for SqlException to catch RAISERROR messages
            try
            {
                var result = await connection.QueryAsync(
                    $"[{schema}].[{procedureName}]",
                    dynamicParams,
                    commandType: CommandType.StoredProcedure
                );

                // Convert result to a list (could be empty if no rows returned)
                var resultList = result.ToList();

                Log.Debug("Successfully executed DELETE procedure for {Endpoint}", endpointName);

                // Return the results, which typically includes deletion confirmation
                return Ok(new
                {
                    success = true,
                    message = "Record deleted successfully",
                    id = id,
                    result = resultList.FirstOrDefault()
                });
            }
            catch (SqlException sqlEx) when (IsIntentionalUserError(sqlEx))
            {
                // Extract the custom error message from RAISERROR
                Log.Warning("Custom SQL error (RAISERROR) for {Endpoint}: {ErrorMessage}", endpointName, sqlEx.Message);

                return BadRequest(new
                {
                    success = false,
                    error = "Bad request",
                    details = sqlEx.Message
                });
            }
        }
        catch (SqlException sqlEx)
        {
            // Handle other SQL exceptions
            Log.Error(sqlEx, "SQL Exception for {Endpoint}: {ErrorCode}, {ErrorMessage}",
                endpointName, sqlEx.Number, sqlEx.Message);

            return CreateErrorResponse("Internal operation failed", null, StatusCodes.Status500InternalServerError);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error processing DELETE for {Endpoint}", endpointName);
            return CreateErrorResponse("An error occurred while processing your request", null, StatusCodes.Status500InternalServerError);
        }
    }


    /// <summary>
    /// Handles Static GET requests
    /// </summary>
    private async Task<IActionResult> HandleStaticGetRequest(
        string env,
        string endpointName,
        string? select,
        string? filter,
        string? orderby,
        int top,
        int skip)
    {
        var url = $"{Request.Scheme}://{Request.Host}{Request.Path}{Request.QueryString}";
        Log.Debug("Static Content Request: {Url}", url);

        try
        {
            // Get static endpoint definition
            var staticEndpoints = EndpointHandler.GetStaticEndpoints();
            if (!staticEndpoints.TryGetValue(endpointName, out var endpoint))
            {
                return NotFound(new { error = $"Endpoint '{endpointName}' not found", success = false });
            }

            // Check environment restrictions
            var (isAllowed, errorResponse) = ValidateEnvironmentRestrictions(env, null, endpointName, EndpointType.Static);
            if (!isAllowed)
            {
                return errorResponse!;
            }

            // Build path to content file - handle namespaced endpoints
            string endpointPath;
            if (endpoint.HasNamespace)
            {
                // For namespaced endpoints, use the full namespace/endpoint structure
                endpointPath = Path.Combine(Directory.GetCurrentDirectory(), "endpoints", "Static",
                    endpoint.EffectiveNamespace!, endpoint.FolderName ?? endpointName);
            }
            else
            {
                // For non-namespaced endpoints, use just the endpoint name
                endpointPath = Path.Combine(Directory.GetCurrentDirectory(), "endpoints", "Static", endpointName);
            }

            var contentFile = endpoint.Properties!["ContentFile"].ToString()!;
            var contentFilePath = Path.Combine(endpointPath, contentFile);

            if (!System.IO.File.Exists(contentFilePath))
            {
                Log.Warning("Content file not found: {FilePath}", contentFilePath);
                return NotFound(new { error = $"Content file not found: {contentFile}", success = false });
            }

            // Get content type and filtering settings
            var contentType = endpoint.Properties["ContentType"].ToString();

            // Auto-detect content type if not specified
            if (string.IsNullOrEmpty(contentType))
            {
                contentType = GetContentTypeFromExtension(contentFile);
                Log.Debug("Auto-detected content type: {ContentType} for file: {ContentFile}", contentType, contentFile);
            }

            var enableFiltering = (bool)(endpoint.Properties.GetValueOrDefault("EnableFiltering", false));

            // Content negotiation: Check if client's Accept header matches our content type
            var acceptHeader = Request.Headers["Accept"].ToString();
            if (!string.IsNullOrEmpty(acceptHeader) && acceptHeader != "*/*")
            {
                var acceptedTypes = acceptHeader.Split(',').Select(t => t.Trim().Split(';')[0]).ToList();

                // Check if our content type is acceptable to the client
                if (!acceptedTypes.Contains(contentType) && !acceptedTypes.Contains("*/*"))
                {
                    Log.Debug("Content negotiation failed: Client accepts {AcceptHeader}, endpoint provides {ContentType}",
                        acceptHeader, contentType);

                    return StatusCode(406, new
                    {
                        error = "Not Acceptable",
                        detail = $"Endpoint provides '{contentType}' but client accepts '{acceptHeader}'",
                        availableContentType = contentType
                    });
                }
            }

            // Read content from file
            var contentBytes = await System.IO.File.ReadAllBytesAsync(contentFilePath);

            // Check if OData filtering is requested and supported
            var hasODataParams = !string.IsNullOrEmpty(select) || !string.IsNullOrEmpty(filter) ||
                               !string.IsNullOrEmpty(orderby) || Request.Query.ContainsKey("$top") || Request.Query.ContainsKey("$skip");

            if (hasODataParams && enableFiltering)
            {
                if (contentType.Contains("json"))
                {
                    // Apply JSON filtering
                    return await ApplyJsonFiltering(contentBytes, contentType, select, filter, orderby, top, skip);
                }
                else if (contentType.Contains("xml"))
                {
                    // Apply XML filtering
                    return await ApplyXmlFiltering(contentBytes, contentType, select, filter, orderby, top, skip);
                }
            }

            Log.Debug("Serving static content: {Endpoint} ({ContentType})", endpointName, contentType);

            return File(contentBytes, contentType);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error serving static content for {Endpoint}", endpointName);
            return StatusCode(500, new { error = "Error serving static content" });
        }
    }

    /// <summary>
    /// Applies JSON filtering using OData-style parameters
    /// </summary>
    private Task<IActionResult> ApplyJsonFiltering(
        byte[] jsonBytes,
        string contentType,
        string? select,
        string? filter,
        string? orderby,
        int top,
        int skip)
    {
        try
        {
            var json = Encoding.UTF8.GetString(jsonBytes);
            var jsonDoc = JsonDocument.Parse(json);
            
            // Start with the root data
            JsonElement data = jsonDoc.RootElement;
            List<JsonElement> items = new List<JsonElement>();
            
            // Handle different JSON structures
            if (data.ValueKind == JsonValueKind.Array)
            {
                // Direct array
                items = data.EnumerateArray().ToList();
            }
            else if (data.ValueKind == JsonValueKind.Object)
            {
                // Look for common array properties
                var arrayProperties = new[] { "data", "items", "results", "value", "users", "records", "countries", "products", "orders", "customers" };
                foreach (var prop in arrayProperties)
                {
                    if (data.TryGetProperty(prop, out var arrayElement) && arrayElement.ValueKind == JsonValueKind.Array)
                    {
                        items = arrayElement.EnumerateArray().ToList();
                        Log.Debug("Found array property '{Property}' with {Count} items", prop, items.Count);
                        break;
                    }
                }
                
                // If no array found, treat the object as a single item
                if (items.Count == 0)
                {
                    Log.Debug("No array property found, treating root object as single item");
                    items.Add(data);
                }
            }
            
            Log.Debug("Applying OData filtering to {Count} items", items.Count);
            
            // Apply filtering
            if (!string.IsNullOrEmpty(filter))
            {
                items = ApplyFilter(items, filter);
                Log.Debug("After filter: {Count} items", items.Count);
            }
            
            // Apply ordering
            if (!string.IsNullOrEmpty(orderby))
            {
                items = ApplyOrderBy(items, orderby);
                Log.Debug("After orderby: {Count} items", items.Count);
            }
            
            // Apply pagination (skip and top)
            var totalCount = items.Count;
            items = items.Skip(skip).Take(top).ToList();
            Log.Debug("After pagination (skip:{Skip}, top:{Top}): {Count} items", skip, top, items.Count);
            
            // Apply field selection
            if (!string.IsNullOrEmpty(select))
            {
                items = ApplySelect(items, select);
                Log.Debug("After select: field selection applied", items.Count);
            }
            
            // Build result in the correct API format
            var result = new
            {
                count = items.Count,
                value = items.Select(SerializeJsonElement).ToArray(),
                nextLink = (string?)null  // Static endpoints don't support pagination links
            };
            
            var resultJson = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
            var resultBytes = Encoding.UTF8.GetBytes(resultJson);
            
            Log.Debug("JSON filtering applied successfully: {Count} items returned", items.Count);

            Response.Headers["X-Filtering-Status"] = "Applied";
            Response.Headers["X-Total-Count"] = totalCount.ToString();
            Response.Headers["X-Returned-Count"] = items.Count.ToString();
            
            return Task.FromResult<IActionResult>(File(resultBytes, contentType));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error applying JSON filtering: {Message}", ex.Message);
            
            // Return error response
            var errorResponse = new
            {
                error = "Internal server error during filtering",
                details = ex.Message,
                originalDataAvailable = true
            };
            
            var errorJson = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions { WriteIndented = true });
            var errorBytes = Encoding.UTF8.GetBytes(errorJson);
            
            Response.Headers["X-Filtering-Status"] = "Error";
            return Task.FromResult<IActionResult>(StatusCode(500, errorResponse));
        }
    }
    
    /// <summary>
    /// Applies OData-style filtering to JSON items
    /// </summary>
    private List<JsonElement> ApplyFilter(List<JsonElement> items, string filter)
    {
        try
        {
            Log.Debug("Parsing filter: {Filter}", filter);
            
            // Parse filter expression (simplified OData filter support). Supports: field eq 'value', field ne 'value', field gt number, field lt number, etc.
            var filterParts = filter.Split(' ');
            if (filterParts.Length >= 3)
            {
                var field = filterParts[0];
                var operation = filterParts[1].ToLower();
                var value = string.Join(" ", filterParts.Skip(2));
                
                // Remove quotes from string values
                value = value.Trim('\'', '"');
                
                Log.Debug("Filter components - Field: {Field}, Operation: {Operation}, Value: {Value}", field, operation, value);
                
                var filteredItems = items.Where(item =>
                {
                    if (!item.TryGetProperty(field, out var fieldValue))
                    {
                        Log.Debug("Field '{Field}' not found in item", field);
                        return false;
                    }
                    
                    Log.Debug("Comparing field '{Field}' value '{FieldValue}' with '{TargetValue}' using operation '{Operation}'", 
                        field, fieldValue, value, operation);
                        
                    var result = operation switch
                    {
                        "eq" => CompareValues(fieldValue, value, "eq"),
                        "ne" => CompareValues(fieldValue, value, "ne"),
                        "gt" => CompareValues(fieldValue, value, "gt"),
                        "lt" => CompareValues(fieldValue, value, "lt"),
                        "ge" => CompareValues(fieldValue, value, "ge"),
                        "le" => CompareValues(fieldValue, value, "le"),
                        "contains" => fieldValue.GetString()?.Contains(value, StringComparison.OrdinalIgnoreCase) == true,
                        "startswith" => fieldValue.GetString()?.StartsWith(value, StringComparison.OrdinalIgnoreCase) == true,
                        "endswith" => fieldValue.GetString()?.EndsWith(value, StringComparison.OrdinalIgnoreCase) == true,
                        _ => false
                    };
                    
                    Log.Debug("Filter result for item: {Result}", result);
                    return result;
                }).ToList();
                
                Log.Debug("Filter matched {Count} items out of {TotalCount}", filteredItems.Count, items.Count);
                return filteredItems;
            }
            
            Log.Warning("Filter expression could not be parsed: {Filter}", filter);
            return items; // Return original if filter couldn't be parsed
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Filter parsing failed, returning unfiltered data");
            return items;
        }
    }
    
    /// <summary>
    /// Compares JSON values for filtering
    /// </summary>
    private bool CompareValues(JsonElement fieldValue, string targetValue, string operation)
    {
        try
        {
            Log.Debug("CompareValues - FieldValue: {FieldValue} ({Type}), TargetValue: {TargetValue}, Operation: {Operation}", 
                fieldValue, fieldValue.ValueKind, targetValue, operation);
            
            switch (fieldValue.ValueKind)
            {
                case JsonValueKind.String:
                    var stringValue = fieldValue.GetString() ?? "";
                    var result = operation switch
                    {
                        "eq" => stringValue.Equals(targetValue, StringComparison.OrdinalIgnoreCase),
                        "ne" => !stringValue.Equals(targetValue, StringComparison.OrdinalIgnoreCase),
                        "gt" => string.Compare(stringValue, targetValue, StringComparison.OrdinalIgnoreCase) > 0,
                        "lt" => string.Compare(stringValue, targetValue, StringComparison.OrdinalIgnoreCase) < 0,
                        "ge" => string.Compare(stringValue, targetValue, StringComparison.OrdinalIgnoreCase) >= 0,
                        "le" => string.Compare(stringValue, targetValue, StringComparison.OrdinalIgnoreCase) <= 0,
                        _ => false
                    };
                    Log.Debug("String comparison result: {Result}", result);
                    return result;
                    
                case JsonValueKind.Number:
                    if (double.TryParse(targetValue, out var targetNumber) && fieldValue.TryGetDouble(out var fieldNumber))
                    {
                        return operation switch
                        {
                            "eq" => Math.Abs(fieldNumber - targetNumber) < 0.0001,
                            "ne" => Math.Abs(fieldNumber - targetNumber) >= 0.0001,
                            "gt" => fieldNumber > targetNumber,
                            "lt" => fieldNumber < targetNumber,
                            "ge" => fieldNumber >= targetNumber,
                            "le" => fieldNumber <= targetNumber,
                            _ => false
                        };
                    }
                    break;
                    
                case JsonValueKind.True:
                case JsonValueKind.False:
                    if (bool.TryParse(targetValue, out var targetBool))
                    {
                        var fieldBool = fieldValue.GetBoolean();
                        return operation switch
                        {
                            "eq" => fieldBool == targetBool,
                            "ne" => fieldBool != targetBool,
                            _ => false
                        };
                    }
                    break;
            }
            
            return false;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Applies OData-style ordering to JSON items
    /// </summary>
    private List<JsonElement> ApplyOrderBy(List<JsonElement> items, string orderby)
    {
        try
        {
            var orderParts = orderby.Split(' ');
            var field = orderParts[0];
            var direction = orderParts.Length > 1 && orderParts[1].ToLower() == "desc" ? "desc" : "asc";
            
            return direction == "desc"
                ? items.OrderByDescending(item => GetSortableValue(item, field)).ToList()
                : items.OrderBy(item => GetSortableValue(item, field)).ToList();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "OrderBy parsing failed, returning original order");
            return items;
        }
    }
    
    /// <summary>
    /// Gets a sortable value from a JSON element
    /// </summary>
    private object GetSortableValue(JsonElement item, string field)
    {
        if (!item.TryGetProperty(field, out var fieldValue))
            return "";
            
        return fieldValue.ValueKind switch
        {
            JsonValueKind.String => fieldValue.GetString() ?? "",
            JsonValueKind.Number => fieldValue.TryGetDouble(out var d) ? d : 0,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => ""
        };
    }
    
    /// <summary>
    /// Applies field selection to JSON items
    /// </summary>
    private List<JsonElement> ApplySelect(List<JsonElement> items, string select)
    {
        try
        {
            var fields = select.Split(',').Select(f => f.Trim()).ToArray();
            var selectedItems = new List<JsonElement>();
            
            foreach (var item in items)
            {
                var selectedObject = new Dictionary<string, object?>();
                
                foreach (var field in fields)
                {
                    if (item.TryGetProperty(field, out var fieldValue))
                    {
                        selectedObject[field] = SerializeJsonElement(fieldValue);
                    }
                }
                
                var json = JsonSerializer.Serialize(selectedObject);
                var element = JsonDocument.Parse(json).RootElement;
                selectedItems.Add(element);
            }
            
            return selectedItems;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Select parsing failed, returning all fields");
            return items;
        }
    }

    /// <summary>
    /// Applies XML filtering using OData-style parameters
    /// </summary>
    private Task<IActionResult> ApplyXmlFiltering(
        byte[] xmlBytes,
        string contentType,
        string? select,
        string? filter,
        string? orderby,
        int top,
        int skip)
    {
        try
        {
            var xmlString = Encoding.UTF8.GetString(xmlBytes);
            var doc = XDocument.Parse(xmlString);
            
            // Find repeating elements (likely the data items to filter)
            var rootElement = doc.Root;
            if (rootElement == null)
            {
                return Task.FromResult<IActionResult>(File(xmlBytes, contentType));
            }

            // Find the main data items - prioritize direct children of root
            var directChildren = rootElement.Elements().ToList();
            List<XElement> items;
            
            if (directChildren.Count > 1)
            {
                // Multiple direct children - these are likely our main data items
                items = directChildren;
                Log.Debug("Found {Count} direct child elements under root '{RootName}'", items.Count, rootElement.Name.LocalName);
            }
            else
            {
                // Look for collections of similar elements at any level
                var elementGroups = rootElement.Descendants()
                    .GroupBy(e => e.Name.LocalName)
                    .Where(g => g.Count() > 1)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault();

                if (elementGroups != null)
                {
                    // Found repeating elements - these are likely our data items
                    items = elementGroups.ToList();
                    Log.Debug("Found {Count} repeating XML elements of type '{ElementName}'", items.Count, elementGroups.Key);
                }
                else
                {
                    // Fallback: treat root as single item
                    items = new List<XElement> { rootElement };
                    Log.Debug("No repeating elements found, treating root as single item");
                }
            }

            var totalCount = items.Count;
            Log.Debug("Applying XML filtering to {Count} elements", totalCount);

            // Apply filtering
            if (!string.IsNullOrEmpty(filter))
            {
                items = ApplyXmlFilter(items, filter);
                Log.Debug("After filter: {Count} items remaining", items.Count);
            }

            // Apply ordering
            if (!string.IsNullOrEmpty(orderby))
            {
                items = ApplyXmlOrderBy(items, orderby);
                Log.Debug("After orderby: items reordered");
            }

            // Apply skip
            if (skip > 0)
            {
                items = items.Skip(skip).ToList();
                Log.Debug("After skip: {Count} items remaining", items.Count);
            }

            // Apply top
            if (top > 0 && top < items.Count)
            {
                items = items.Take(top).ToList();
                Log.Debug("After top: {Count} items selected", items.Count);
            }

            // Apply select (field selection)
            if (!string.IsNullOrEmpty(select))
            {
                items = ApplyXmlSelect(items, select);
                Log.Debug("After select: field selection applied");
            }

            // Rebuild XML with filtered items
            XDocument resultDoc;
            if (directChildren.Count > 1)
            {
                // Create new document with same structure but filtered items
                resultDoc = new XDocument(doc.Declaration);
                var newRoot = new XElement(rootElement.Name, rootElement.Attributes());
                
                // Add filtered items back to the root
                foreach (var item in items)
                {
                    newRoot.Add(new XElement(item));
                }
                
                resultDoc.Add(newRoot);
            }
            else
            {
                // Simple structure - just replace root children
                resultDoc = new XDocument(doc.Declaration);
                var newRoot = new XElement(rootElement.Name, rootElement.Attributes());
                newRoot.Add(items);
                resultDoc.Add(newRoot);
            }

            var resultXml = resultDoc.ToString();
            var resultBytes = Encoding.UTF8.GetBytes(resultXml);
            
            Log.Debug("XML filtering applied successfully: {Count} items returned out of {Total}", items.Count, totalCount);

            Response.Headers["X-Filtering-Status"] = "Applied";
            Response.Headers["X-Total-Count"] = totalCount.ToString();
            Response.Headers["X-Returned-Count"] = items.Count.ToString();
            
            return Task.FromResult<IActionResult>(File(resultBytes, contentType));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error applying XML filtering: {Message}", ex.Message);
            
            // Fallback to original content
            Response.Headers["X-Filtering-Status"] = "Error";
            return Task.FromResult<IActionResult>(File(xmlBytes, contentType));
        }
    }

    /// <summary>
    /// Serializes a JsonElement to an object for JSON output
    /// </summary>
    private object? SerializeJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => SerializeJsonElement(p.Value)),
            JsonValueKind.Array => element.EnumerateArray().Select(SerializeJsonElement).ToArray(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt32(out var i) ? i : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.ToString()
        };
    }

    /// <summary>
    /// Auto-detects content type based on file extension
    /// </summary>
    private string GetContentTypeFromExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        
        return extension switch
        {
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".html" => "text/html",
            ".htm" => "text/html",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".txt" => "text/plain",
            ".csv" => "text/csv",
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".webp" => "image/webp",
            ".ico" => "image/x-icon",
            ".pdf" => "application/pdf",
            ".zip" => "application/zip",
            ".mp4" => "video/mp4",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            _ => "application/octet-stream"
        };
    }

    /// <summary>
    /// Helper method to create a standard error response
    /// </summary>
    private IActionResult CreateErrorResponse(string message, string? detail = null, int statusCode = 400)
    {
        var response = new
        {
            success = false,
            error = message,
            errorDetail = detail,
            timestamp = DateTime.UtcNow
        };
        
        return StatusCode(statusCode, response);
    }

    /// <summary>
    /// Validates SQL input data against allowed columns, required columns, and regex patterns
    /// </summary>
    private (bool IsValid, string? ErrorMessage, List<ValidationError>? Errors) ValidateSqlInput(
        JsonElement data,
        EndpointDefinition endpoint,
        string httpMethod)
    {
        var errors = new List<ValidationError>();

        // Check for empty request body
        if (data.ValueKind == JsonValueKind.Undefined || data.ValueKind == JsonValueKind.Null)
        {
            return (false, "Request body cannot be empty", null);
        }

        // Check required columns for POST (CREATE) operations
        if (httpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase) && endpoint.RequiredColumns != null)
        {
            foreach (var requiredColumn in endpoint.RequiredColumns)
            {
                bool hasProperty = data.TryGetProperty(requiredColumn, out var propValue);
                bool isEmpty = !hasProperty ||
                            propValue.ValueKind == JsonValueKind.Null ||
                            (propValue.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(propValue.GetString()));

                if (isEmpty)
                {
                    errors.Add(new ValidationError
                    {
                        Field = requiredColumn,
                        Message = $"{requiredColumn} is required"
                    });
                }
            }
        }

        // Check allowed columns (with alias support)
        if (endpoint.AllowedColumns != null && endpoint.AllowedColumns.Count > 0)
        {
            // Parse allowed columns to handle aliases (e.g., "EmployeeId;ID")
            var allowedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in endpoint.AllowedColumns)
            {
                var parts = column.Split(';');
                allowedFields.Add(parts[0]); // Add primary column name
                if (parts.Length > 1)
                {
                    allowedFields.Add(parts[1]); // Add alias if exists
                }
            }
            
            foreach (var property in data.EnumerateObject())
            {
                if (!allowedFields.Contains(property.Name))
                {
                    errors.Add(new ValidationError
                    {
                        Field = property.Name,
                        Message = $"{property.Name} is not an allowed property"
                    });
                }
            }
        }
        // Validate regex patterns
        if (endpoint.ColumnValidation != null)
        {
            foreach (var validation in endpoint.ColumnValidation)
            {
                string columnName = validation.Key;

                if (data.TryGetProperty(columnName, out var propValue) &&
                    propValue.ValueKind == JsonValueKind.String)
                {
                    string? value = propValue.GetString();

                    if (!string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(validation.Value.Regex))
                    {
                        try
                        {
                            var regex = new System.Text.RegularExpressions.Regex(validation.Value.Regex);

                            if (!regex.IsMatch(value))
                            {
                                errors.Add(new ValidationError
                                {
                                    Field = columnName,
                                    Message = string.IsNullOrEmpty(validation.Value.ValidationMessage)
                                        ? $"{columnName} does not match the required format"
                                        : validation.Value.ValidationMessage
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "Invalid regex pattern for column {Column}", columnName);
                        }
                    }
                }
            }
        }

        if (errors.Any())
        {
            return (false, "Validation failed", errors);
        }

        return (true, null, null);
    }
    
    /// <summary>
    /// Common ID field names for validation
    /// </summary>
    private static readonly string[] IdFieldNames = new[]
    {
        "id", "Id", "ID", "IdField", "IDField",
        "pk", "PK", "PrimaryKey", "primaryKey", "primarykey",
        "internalId", "InternalId", "InternalID", "internalid",
        "recordId", "RecordId"
    };

    /// <summary>
    /// Validates SQL parameters for update and delete operations
    /// </summary>
    private (bool IsValid, string? ErrorMessage) ValidateSqlParameters(JsonElement data, string operation)
    {

        if (operation is "UPDATE" or "DELETE")
        {
            bool hasId = IdFieldNames.Any(fieldName => data.TryGetProperty(fieldName, out _));
                        
            if (!hasId)
            {
                return (false, "ID field is required for this operation");
            }
        }
        
        return (true, null);
    }

    private class ValidationError
    {
        public string Field { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public class RequestValidationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<RequestValidationMiddleware> _logger;

        public RequestValidationMiddleware(RequestDelegate next, ILogger<RequestValidationMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Check content type for POST/PUT/PATCH
            if (HttpMethods.IsPost(context.Request.Method) || 
                HttpMethods.IsPut(context.Request.Method) || 
                HttpMethods.IsPatch(context.Request.Method))
            {
                string? contentType = context.Request.ContentType;
                
                if (string.IsNullOrEmpty(contentType) || !contentType.Contains("application/json"))
                {
                    _logger.LogWarning("Invalid content type: {ContentType}", contentType);
                    
                    context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
                    context.Response.ContentType = "application/json";
                    
                    await context.Response.WriteAsJsonAsync(new
                    {
                        error = "Unsupported Media Type",
                        detail = "Request must use application/json content type",
                        success = false
                    });
                    
                    return;
                }
                
                // Check content length
                if (context.Request.ContentLength > 52_428_800) // 10MB limit
                {
                    _logger.LogWarning("Request body too large: {ContentLength} bytes", context.Request.ContentLength);
                    
                    context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                    context.Response.ContentType = "application/json";
                    
                    await context.Response.WriteAsJsonAsync(new
                    {
                        error = "Payload Too Large",
                        detail = "Request body exceeds maximum size of 50MB",
                        success = false
                    });
                    
                    return;
                }
            }

            await _next(context);
        }
    }

    /// <summary>
    /// Applies XML filtering using OData $filter syntax
    /// </summary>
    private List<XElement> ApplyXmlFilter(List<XElement> items, string filter)
    {
        try
        {
            Log.Debug("Parsing XML filter: {Filter}", filter);
            
            // Simple filter implementations for common patterns
            if (filter.Contains(" eq "))
            {
                // Handle equality: field eq 'value'
                var parts = filter.Split(" eq ");
                if (parts.Length == 2)
                {
                    var fieldName = parts[0].Trim();
                    var fieldValue = parts[1].Trim().Trim('\'').Trim('"');
                    
                    return items.Where(item => 
                    {
                        var element = item.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
                        return element?.Value?.Equals(fieldValue, StringComparison.OrdinalIgnoreCase) == true;
                    }).ToList();
                }
            }
            else if (filter.Contains(" contains "))
            {
                // Handle contains: contains(field, 'value')
                var containsMatch = Regex.Match(filter, @"contains\((\w+),\s*['""]([^'""]+)['""]", RegexOptions.IgnoreCase);
                if (containsMatch.Success)
                {
                    var fieldName = containsMatch.Groups[1].Value;
                    var fieldValue = containsMatch.Groups[2].Value;
                    
                    return items.Where(item => 
                    {
                        var element = item.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
                        return element?.Value?.Contains(fieldValue, StringComparison.OrdinalIgnoreCase) == true;
                    }).ToList();
                }
                
                // Handle simple contains: field contains 'value'
                var simpleParts = filter.Split(" contains ");
                if (simpleParts.Length == 2)
                {
                    var fieldName = simpleParts[0].Trim();
                    var fieldValue = simpleParts[1].Trim().Trim('\'').Trim('"');
                    
                    return items.Where(item => 
                    {
                        var element = item.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
                        return element?.Value?.Contains(fieldValue, StringComparison.OrdinalIgnoreCase) == true;
                    }).ToList();
                }
            }
            
            Log.Warning("Unsupported XML filter pattern: {Filter}", filter);
            return items;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "XML filter parsing failed: {Filter}, returning unfiltered results", filter);
            return items;
        }
    }

    /// <summary>
    /// Applies XML ordering using OData $orderby syntax
    /// </summary>
    private List<XElement> ApplyXmlOrderBy(List<XElement> items, string orderby)
    {
        try
        {
            Log.Debug("Parsing XML orderby: {OrderBy}", orderby);
            
            var parts = orderby.Split(' ');
            var fieldName = parts[0].Trim();
            var direction = parts.Length > 1 && parts[1].Trim().Equals("desc", StringComparison.OrdinalIgnoreCase) ? "desc" : "asc";
            
            if (direction == "desc")
            {
                return items.OrderByDescending(item =>
                {
                    var element = item.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
                    return element?.Value ?? "";
                }).ToList();
            }
            else
            {
                return items.OrderBy(item =>
                {
                    var element = item.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
                    return element?.Value ?? "";
                }).ToList();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "XML orderby parsing failed: {OrderBy}, returning original order", orderby);
            return items;
        }
    }

    /// <summary>
    /// Applies XML field selection using OData $select syntax
    /// </summary>
    private List<XElement> ApplyXmlSelect(List<XElement> items, string select)
    {
        try
        {
            Log.Debug("Parsing XML select: {Select}", select);
            
            var fields = select.Split(',').Select(f => f.Trim()).ToList();
            var selectedItems = new List<XElement>();
            
            foreach (var item in items)
            {
                var newItem = new XElement(item.Name);
                
                // Copy attributes
                foreach (var attr in item.Attributes())
                {
                    newItem.Add(new XAttribute(attr));
                }
                
                // Add only selected fields
                foreach (var field in fields)
                {
                    var elements = item.Descendants().Where(e => e.Name.LocalName.Equals(field, StringComparison.OrdinalIgnoreCase));
                    foreach (var element in elements)
                    {
                        // Only add direct children, not nested descendants
                        if (element.Parent == item)
                        {
                            newItem.Add(new XElement(element));
                        }
                    }
                }
                
                selectedItems.Add(newItem);
            }
            
            return selectedItems;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "XML select parsing failed: {Select}, returning all fields", select);
            return items;
        }
    }

    #region Endpoint Property Override Helpers

    /// <summary>
    /// Applies the MaxPageSize limit from endpoint properties, overriding the requested top value if necessary
    /// </summary>
    private int ApplyMaxPageSizeLimit(int requestedTop, EndpointDefinition endpoint)
    {
        // Check if MaxPageSize is defined in endpoint properties
        if (endpoint.Properties?.TryGetValue("MaxPageSize", out var maxPageSizeObj) == true)
        {
            if (maxPageSizeObj is int maxPageSize)
            {
                Log.Debug("📏 Applying MaxPageSize limit: {MaxPageSize} for endpoint {Endpoint}", maxPageSize, endpoint.EndpointName);
                return Math.Min(requestedTop, maxPageSize);
            }
            else if (int.TryParse(maxPageSizeObj?.ToString(), out var parsedMaxPageSize))
            {
                Log.Debug("📏 Applying MaxPageSize limit: {MaxPageSize} for endpoint {Endpoint}", parsedMaxPageSize, endpoint.EndpointName);
                return Math.Min(requestedTop, parsedMaxPageSize);
            }
        }
        
        return requestedTop; // No limit defined, use original value
    }

    /// <summary>
    /// Applies the DefaultSort from endpoint properties if no orderby is provided
    /// </summary>
    private string? ApplyDefaultSorting(string? requestedOrderBy, EndpointDefinition endpoint)
    {
        // Only apply default if no orderby was requested
        if (!string.IsNullOrEmpty(requestedOrderBy))
        {
            return requestedOrderBy;
        }

        // Check if DefaultSort is defined in endpoint properties
        if (endpoint.Properties?.TryGetValue("DefaultSort", out var defaultSortObj) == true)
        {
            var defaultSort = defaultSortObj?.ToString();
            if (!string.IsNullOrEmpty(defaultSort))
            {
                Log.Debug("🔀 Applying DefaultSort: {DefaultSort} for endpoint {Endpoint}", defaultSort, endpoint.EndpointName);
                return defaultSort;
            }
        }

        return requestedOrderBy; // No default defined, use original value
    }

    /// <summary>
    /// Checks if caching is enabled for the endpoint
    /// </summary>
    private bool IsCacheEnabled(EndpointDefinition endpoint)
    {
        if (endpoint.Properties?.TryGetValue("CacheEnabled", out var cacheEnabledObj) == true)
        {
            if (cacheEnabledObj is bool cacheEnabled)
            {
                return cacheEnabled;
            }
            else if (bool.TryParse(cacheEnabledObj?.ToString(), out var parsedCacheEnabled))
            {
                return parsedCacheEnabled;
            }
        }
        
        return false; // Default: caching disabled
    }

    /// <summary>
    /// Gets the cache duration in minutes for the endpoint
    /// </summary>
    private int GetCacheDurationMinutes(EndpointDefinition endpoint)
    {
        if (endpoint.Properties?.TryGetValue("CacheDurationMinutes", out var durationObj) == true)
        {
            if (durationObj is int duration)
            {
                return duration;
            }
            else if (int.TryParse(durationObj?.ToString(), out var parsedDuration))
            {
                return parsedDuration;
            }
        }
        
        return 5; // Default: 5 minutes
    }

    /// <summary>
    /// Gets the DELETE pattern for a proxy endpoint (with default fallback)
    /// </summary>
    private DeletePattern GetDeletePatternForProxy(string endpointName)
    {
        var proxyEndpoints = EndpointHandler.GetProxyEndpoints();
        
        if (proxyEndpoints.TryGetValue(endpointName, out var definition) 
            && definition.DeletePatterns?.Any() == true)
        {
            return definition.DeletePatterns.First();
        }
        
        // Default fallback
        return new DeletePattern 
        { 
            Style = "PathParameter",
            Description = "Delete by ID in path (default)"
        };
    }

#endregion

#endregion
}