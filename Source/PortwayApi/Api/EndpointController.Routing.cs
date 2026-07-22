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
    /// <summary>Handles GET requests to endpoints</summary>
    [HttpGet("{env}/{**catchall}")]
    [ResponseCache(Duration = 300, VaryByHeader = "Authorization")]
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
                    return PortwayResults.MethodNotAllowed(this);
                case EndpointType.Webhook:
                    // Log warning and return 405
                    Log.Warning("Webhook endpoints don't support GET requests");
                    return PortwayResults.MethodNotAllowed(this);
                default:
                    // Log warning and return 404
                    Log.Warning("Unknown endpoint type for {EndpointName}", endpointName);
                    return PortwayResults.NotFound(this, $"Endpoint '{endpointName}' not found");
            }
        }
        catch (Exception ex)
        {
            return HandleUnexpectedProblem(ex, "GET");
        }
    }

    /// <summary>Handles QUERY requests (RFC 10008): a safe, idempotent, cacheable read whose query lives in the request body</summary>
    [AcceptVerbs("QUERY", Route = "{env}/{**catchall}")]
    [ResponseCache(Duration = 300, VaryByHeader = "Authorization")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status405MethodNotAllowed)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> QueryAsync(string env, string catchall)
    {
        try
        {
            // RFC 10008: servers MUST fail the request if Content-Type is missing or inconsistent with the content
            var contentType = Request.ContentType ?? string.Empty;
            if (!contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
            {
                return PortwayResults.UnsupportedMediaType(this, "QUERY requires Content-Type: application/json");
            }

            // Buffer the body so proxy endpoints can re-read it after we parse the query content
            Request.EnableBuffering();
            string requestBody;
            using (var reader = new StreamReader(Request.Body, leaveOpen: true))
            {
                requestBody = await reader.ReadToEndAsync();
            }
            Request.Body.Position = 0;

            QueryBody? queryParams;
            try
            {
                queryParams = ParseQueryBody(requestBody);
            }
            catch (JsonException)
            {
                return PortwayResults.BadRequest(this, "Invalid JSON in QUERY body");
            }
            if (queryParams == null)
            {
                return PortwayResults.BadRequest(this, "QUERY body must be a JSON object");
            }

            var (endpointType, namespaceName, endpointName, id, remainingPath) = ParseEndpoint(catchall);
            Log.Debug("Processing {Type} endpoint: {Name} for QUERY", endpointType, endpointName);

            var (isAllowed, errorResponse) = ValidateEnvironmentRestrictions(env, namespaceName, endpointName, endpointType);
            if (!isAllowed)
            {
                return errorResponse!;
            }

            switch (endpointType)
            {
                case EndpointType.SQL:
                    var sqlKey = !string.IsNullOrEmpty(namespaceName) ? $"{namespaceName}/{endpointName}" : endpointName;
                    // RFC 10008: advertise the equivalent GET representation of this query
                    SetQueryContentLocation(env, sqlKey, queryParams);
                    return await HandleSqlGetRequest(env, sqlKey, id, remainingPath,
                        queryParams.Select, queryParams.Filter, queryParams.OrderBy,
                        queryParams.Top ?? 10, queryParams.Skip ?? 0, httpMethod: "QUERY");

                case EndpointType.Static:
                    var staticKey = !string.IsNullOrEmpty(namespaceName) ? $"{namespaceName}/{endpointName}" : endpointName;
                    return await HandleStaticGetRequest(env, staticKey,
                        queryParams.Select, queryParams.Filter, queryParams.OrderBy,
                        queryParams.Top ?? 10, queryParams.Skip ?? 0);

                case EndpointType.Proxy:
                    var proxyKey = !string.IsNullOrEmpty(namespaceName) ? $"{namespaceName}/{endpointName}" : endpointName;
                    return await HandleProxyRequest(env, proxyKey, id, remainingPath, "QUERY");

                case EndpointType.Composite:
                case EndpointType.Webhook:
                    Log.Warning("{Type} endpoints don't support QUERY requests", endpointType);
                    return PortwayResults.MethodNotAllowed(this);

                default:
                    Log.Warning("Unknown or unsupported endpoint type for QUERY: {EndpointName}", endpointName);
                    return PortwayResults.NotFound(this, $"Endpoint '{endpointName}' not found");
            }
        }
        catch (Exception ex)
        {
            return HandleUnexpectedProblem(ex, "QUERY");
        }
    }

    /// <summary>OData-style parameters carried in a QUERY request body; accepts both bare and $-prefixed keys</summary>
    private sealed class QueryBody
    {
        public string? Select { get; init; }
        public string? Filter { get; init; }
        public string? OrderBy { get; init; }
        public int? Top { get; init; }
        public int? Skip { get; init; }
    }

    /// <summary>Parses a QUERY JSON body into OData parameters. Returns null when the body is not a JSON object; empty body yields defaults</summary>
    private static QueryBody? ParseQueryBody(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new QueryBody();
        }

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var root = doc.RootElement;

        string? Str(string a, string b) =>
            root.TryGetProperty(a, out var va) && va.ValueKind == JsonValueKind.String ? va.GetString()
            : root.TryGetProperty(b, out var vb) && vb.ValueKind == JsonValueKind.String ? vb.GetString()
            : null;

        int? Num(string a, string b)
        {
            if (root.TryGetProperty(a, out var va) && va.ValueKind == JsonValueKind.Number && va.TryGetInt32(out var na)) return na;
            if (root.TryGetProperty(b, out var vb) && vb.ValueKind == JsonValueKind.Number && vb.TryGetInt32(out var nb)) return nb;
            return null;
        }

        return new QueryBody
        {
            Select = Str("$select", "select"),
            Filter = Str("$filter", "filter"),
            OrderBy = Str("$orderby", "orderby"),
            Top = Num("$top", "top"),
            Skip = Num("$skip", "skip")
        };
    }

    /// <summary>Sets Content-Location to the equivalent GET URL for a SQL QUERY (RFC 10008), when the query is URL-representable</summary>
    private void SetQueryContentLocation(string env, string endpointPath, QueryBody q)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(q.Select)) parts.Add($"$select={Uri.EscapeDataString(q.Select)}");
        if (!string.IsNullOrEmpty(q.Filter)) parts.Add($"$filter={Uri.EscapeDataString(q.Filter)}");
        if (!string.IsNullOrEmpty(q.OrderBy)) parts.Add($"$orderby={Uri.EscapeDataString(q.OrderBy)}");
        if (q.Top.HasValue) parts.Add($"$top={q.Top.Value}");
        if (q.Skip.HasValue) parts.Add($"$skip={q.Skip.Value}");

        var url = $"/api/{env}/{endpointPath}";
        if (parts.Count > 0) url += "?" + string.Join('&', parts);
        Response.Headers["Content-Location"] = url;
    }

    /// <summary>Handles HEAD requests to static endpoints</summary>
    [HttpHead("{env}/{**catchall}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status405MethodNotAllowed)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public IActionResult Head(string env, string catchall)
    {
        try
        {
            // Process the catchall to determine endpoint type
            var (endpointType, namespaceName, endpointName, id, remainingPath) = ParseEndpoint(catchall);
            
            // Only support HEAD for static endpoints
            if (endpointType != EndpointType.Static)
            {
                return PortwayResults.MethodNotAllowed(this, "HEAD method is only supported for static endpoints");
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
                contentType = StaticRequestHandler.GetContentTypeFromExtension(contentFile);
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
            return HandleUnexpectedError(ex, "HEAD", Request.Path, "Error processing HEAD request. Please check the logs for more details.");
        }
    }

    /// <summary>Handles POST requests to endpoints</summary>
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

            // Legacy webhook route was removed in favour of namespaced webhooks; return a clear tombstone
            if (endpointType == EndpointType.Standard &&
                string.Equals(endpointName, "webhook", StringComparison.OrdinalIgnoreCase))
            {
                Log.Warning("Legacy webhook route '/api/{Env}/webhook/...' is no longer supported", env);
                return StatusCode(StatusCodes.Status410Gone, ErrorResponse.Of(
                    "The '/api/{env}/webhook/{id}' route was removed. Webhooks are now namespaced: use '/api/{env}/{namespace}/{name}/{id}'."));
            }

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
                    // The webhook id (topic selector) is the segment after the endpoint: prefer parsed id, fall back to remaining path
                    string webhookId = id ?? remainingPath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
                    if (string.IsNullOrEmpty(webhookId))
                    {
                        return PortwayResults.BadRequest(this, "Webhook id is required: use '/api/{env}/{namespace}/{name}/{id}'.");
                    }
                    var webhookData = JsonSerializer.Deserialize<JsonElement>(requestBody);
                    return await HandleWebhookRequest(env, webhookKey, webhookId, webhookData);
                    
                default:
                    Log.Warning("Unknown endpoint type for {EndpointName}", endpointName);
                    return PortwayResults.NotFound(this, $"Endpoint '{endpointName}' not found");
            }
        }
        catch (Exception ex)
        {
            return HandleUnexpectedProblem(ex, "POST");
        }
    }

    /// <summary>Handles PUT requests to endpoints</summary>
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

            switch (endpointType)
            {
                case EndpointType.SQL:
                    string requestBody;
                    using (var reader = new StreamReader(Request.Body))
                    {
                        requestBody = await reader.ReadToEndAsync();
                    }
                    var data = JsonSerializer.Deserialize<JsonElement>(requestBody);
                    var sqlKey = !string.IsNullOrEmpty(namespaceName) ? $"{namespaceName}/{endpointName}" : endpointName;
                    return await HandleSqlPutRequest(env, sqlKey, data);

                case EndpointType.Proxy:
                    var proxyKey = !string.IsNullOrEmpty(namespaceName) ? $"{namespaceName}/{endpointName}" : endpointName;
                    return await HandleProxyRequest(env, proxyKey, id, remainingPath, "PUT");

                default:
                    Log.Warning("{Type} endpoints don't support PUT requests", endpointType);
                    return PortwayResults.MethodNotAllowed(this);
            }
        }
        catch (Exception ex)
        {
            return HandleUnexpectedProblem(ex, "PUT");
        }
    }

    /// <summary>Handles DELETE requests to endpoints</summary>
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
        string catchall)
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
                        return PortwayResults.BadRequest(this, "ID parameter is required for delete operations");
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
                    return PortwayResults.MethodNotAllowed(this);

                case EndpointType.Webhook:
                    Log.Warning("{Type} endpoints don't support DELETE requests", endpointType);
                    return PortwayResults.MethodNotAllowed(this);

                default:
                    Log.Warning("Unknown endpoint type for {EndpointName}", endpointName);
                    return PortwayResults.NotFound(this, $"Endpoint '{endpointName}' not found");
            }
        }
        catch (Exception ex)
        {
            return HandleUnexpectedProblem(ex, "DELETE");
        }
    }

    /// <summary>Handles PATCH requests to endpoints</summary>
    [HttpPatch("{env}/{**catchall}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status405MethodNotAllowed)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
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

            switch (endpointType)
            {
                case EndpointType.Proxy:
                    var proxyKey = !string.IsNullOrEmpty(namespaceName) ? $"{namespaceName}/{endpointName}" : endpointName;
                    return await HandleProxyRequest(env, proxyKey, id, remainingPath, "PATCH");

                case EndpointType.SQL:
                    var sqlKey = !string.IsNullOrEmpty(namespaceName) ? $"{namespaceName}/{endpointName}" : endpointName;
                    JsonDocument requestBody;
                    try
                    {
                        requestBody = await JsonDocument.ParseAsync(Request.Body);
                    }
                    catch (JsonException)
                    {
                        return PortwayResults.BadRequest(this, "Invalid JSON format in request");
                    }
                    using (requestBody)
                    {
                        return await HandleSqlPatchRequest(env, sqlKey, requestBody);
                    }

                default:
                    Log.Warning("{Type} endpoints don't support PATCH requests", endpointType);
                    return PortwayResults.MethodNotAllowed(this);
            }
        }
        catch (Exception ex)
        {
            return HandleUnexpectedProblem(ex, "PATCH");
        }
    }

}
