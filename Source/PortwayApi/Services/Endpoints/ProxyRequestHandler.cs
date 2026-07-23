namespace PortwayApi.Services;

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using PortwayApi.Classes;
using PortwayApi.Helpers;
using PortwayApi.Interfaces;
using Serilog;

/// <summary>Forwards proxy endpoint requests upstream with caching, retry and failover</summary>
public sealed class ProxyRequestHandler
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly UrlValidator _urlValidator;
    private readonly IEnvironmentSettingsProvider _environmentSettingsProvider;
    private readonly Services.Caching.CacheManager _cacheManager;

    public ProxyRequestHandler(
        IHttpClientFactory httpClientFactory,
        UrlValidator urlValidator,
        IEnvironmentSettingsProvider environmentSettingsProvider,
        Services.Caching.CacheManager cacheManager)
    {
        _httpClientFactory = httpClientFactory;
        _urlValidator = urlValidator;
        _environmentSettingsProvider = environmentSettingsProvider;
        _cacheManager = cacheManager;
    }

    /// <summary>Handles proxy requests for any HTTP method with request caching</summary>
    private static readonly MemoryCache _proxyCache = new MemoryCache(new MemoryCacheOptions());

    private static readonly HashSet<string> _noReservedParams = new(StringComparer.OrdinalIgnoreCase);

    // Endpoint Urls are config-static, so the path/query split and reserved param names are computed once per Url
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string Path, string Query, HashSet<string> Reserved)> _splitUrlCache = new();

    private static (string Path, string Query, HashSet<string> Reserved) SplitEndpointUrl(string url) =>
        _splitUrlCache.GetOrAdd(url, static u =>
        {
            var queryIndex = u.IndexOf('?');
            if (queryIndex < 0)
                return (u, string.Empty, _noReservedParams);

            var query = u[(queryIndex + 1)..];
            var reserved = query.Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split('=', 2)[0])
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return (u[..queryIndex], query, reserved);
        });

    public async Task<IActionResult> HandleProxyRequest(
        HttpContext context,
        EndpointDefinition endpointDefinition,
        string env,
        string endpointName,
        string? id,
        string remainingPath,
        string method)
    {
        Log.Debug("Handling proxy request: {Endpoint} {Method}", endpointName, method);

        try
        {
            // Translate HTTP method if configured
            var originalMethod = method;
            var incomingMethod = context.Request.Method ?? originalMethod;
            var translatedMethod = PortwayApi.Helpers.HttpMethodTranslator.TranslateMethod(incomingMethod, endpointDefinition.CustomProperties);
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
                return PortwayResults.MethodNotAllowed();
            }

            // Validate translated method
            if (!PortwayApi.Helpers.HttpMethodTranslator.IsValidHttpMethod(translatedMethod))
            {
                Log.Warning("Translated method {TranslatedMethod} is not valid for endpoint {EndpointName}",
                    translatedMethod, endpointName);
                return PortwayResults.ServerError(context, "Invalid translated HTTP method");
            }

            // Convert endpoint definition to tuple format for legacy compatibility
            var endpointConfig = endpointDefinition.ToProxyEndpointInfo();

            // Construct full URL
            var queryString = context.Request.QueryString.Value ?? string.Empty;
            // Detach any query baked into the endpoint Url so ids and path segments append to the path; re-attached after construction
            var (fullUrl, baseQuery, reservedParams) = SplitEndpointUrl(endpointConfig.Url ?? string.Empty);

            // Special handling for DELETE method based on DeletePattern
            if (method.Equals("DELETE", StringComparison.OrdinalIgnoreCase))
            {
                var delEncodedId = Uri.EscapeDataString(id ?? string.Empty);
                fullUrl = ConstructDeleteUrl(fullUrl, delEncodedId, remainingPath, endpointName);
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

            // Re-attach the endpoint Url's own query; DELETE QueryParameter style may already
            // have introduced a '?', so merge rather than blindly prepend
            if (!string.IsNullOrEmpty(baseQuery))
            {
                fullUrl += fullUrl.Contains('?') ? "&" + baseQuery : "?" + baseQuery;
            }

            // Append query string safely (avoid double '?' and preserve existing query params)
            if (!string.IsNullOrEmpty(queryString))
            {
                // context.Request.QueryString.Value begins with '?' when non-empty
                var qs = queryString.StartsWith("?") ? queryString.Substring(1) : queryString;

                // Drop client params that collide with baked ones; upstreams resolving duplicates last-wins would let clients override injected credentials
                if (reservedParams.Count > 0)
                {
                    qs = string.Join('&', qs.Split('&', StringSplitOptions.RemoveEmptyEntries)
                        .Where(p => !reservedParams.Contains(p.Split('=', 2)[0])));
                }

                if (!string.IsNullOrEmpty(qs))
                    fullUrl += fullUrl.Contains('?') ? "&" + qs : "?" + qs;
            }

            Log.Debug("Final constructed URL: {Url}", fullUrl);

            // Store the target URL in the context items for logging
            context.Items["TargetUrl"] = fullUrl;

            // Validate URL safety
            if (!_urlValidator.IsUrlSafe(fullUrl))
            {
                Log.Warning("Blocked URL due to security restrictions: {Url}", fullUrl);
                return new ObjectResult(ErrorResponse.Of("Request blocked due to security restrictions")) { StatusCode = 403 };
            }

            // Detect if this is likely a SOAP request
            bool isSoapRequest = (context.Request.ContentType?.Contains("text/xml") == true) ||
                                 (context.Request.ContentType?.Contains("application/soap+xml") == true) ||
                                 (fullUrl.Contains(".svc") && !fullUrl.Contains("REST")) ||
                                 (context.Request.Headers?.ContainsKey("SOAPAction") == true);

            if (isSoapRequest)
            {
                Log.Debug("Detected SOAP request for endpoint: {Endpoint}", endpointName);
                // SOAP requests generally shouldn't be cached, so bypass cache and execute directly
                await ExecuteProxyRequest(context, translatedMethod, fullUrl, env, endpointConfig, endpointName, isSoapRequest: true, originalMethod: originalMethod, endpointDefinition: endpointDefinition);
                return new EmptyResult(); // Response already written
            }

            // GET and QUERY are both safe and cacheable (RFC 10008 for QUERY)
            if (originalMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) ||
                originalMethod.Equals("QUERY", StringComparison.OrdinalIgnoreCase))
            {
                // Create a cache key based on the request details
                var headers = context.Request.Headers ?? new Microsoft.AspNetCore.Http.HeaderDictionary();
                string cacheKey = CreateCacheKey(env, endpointName, remainingPath ?? string.Empty, queryString, headers);

                // RFC 10008: the cache key for a QUERY request MUST incorporate the request content
                if (originalMethod.Equals("QUERY", StringComparison.OrdinalIgnoreCase))
                {
                    cacheKey += ":q=" + await ComputeBodyHashAsync(context);
                }

                // Try to get from cache first
                var cacheEntry = await _cacheManager.GetAsync<Services.Caching.ProxyCacheEntry>(cacheKey);
                
                if (cacheEntry != null)
                {
                    Log.Debug("Cache hit for proxy request: {Endpoint}, URL: {Url}", endpointName, fullUrl);
                    
                    // Apply cached headers and status code
                    foreach (var header in cacheEntry.Headers)
                    {
                        context.Response.Headers[header.Key] = header.Value;
                    }
                    
                    context.Response.StatusCode = cacheEntry.StatusCode;
                    
                    // Write cached content
                    await context.Response.WriteAsync(cacheEntry.Content);
                    
                    return new EmptyResult(); // Response already written
                }
                
                Log.Debug("Cache miss for proxy request: {Endpoint}, URL: {Url}", endpointName, fullUrl);
                
                // Acquire a distributed lock to prevent duplicate requests
                using var lockHandle = await _cacheManager.AcquireLockAsync(
                    cacheKey,
                    TimeSpan.FromSeconds(30),
                    TimeSpan.FromSeconds(10),
                    TimeSpan.FromMilliseconds(200),
                    context.RequestAborted);
                
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
                            context.Response.Headers[header.Key] = header.Value;
                        }
                        
                        context.Response.StatusCode = cacheEntry.StatusCode;
                        
                        // Write cached content
                        await context.Response.WriteAsync(cacheEntry.Content);
                        
                        return new EmptyResult(); // Response already written
                    }
                    
                    // Continue with normal proxy process for cache miss
                    var responseDetails = await ExecuteProxyRequest(context, translatedMethod, fullUrl, env, endpointConfig, endpointName, originalMethod: originalMethod, endpointDefinition: endpointDefinition);
                    
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
                    Log.Warning("Could not acquire lock for caching: {Endpoint}", endpointName);
                    await ExecuteProxyRequest(context, translatedMethod, fullUrl, env, endpointConfig, endpointName, originalMethod: originalMethod, endpointDefinition: endpointDefinition);
                }
                
                return new EmptyResult(); // Response already written
            }
            else
            {
                // For non-GET requests, just execute the proxy request without caching
                await ExecuteProxyRequest(context, translatedMethod, fullUrl, env, endpointConfig, endpointName, originalMethod: originalMethod, endpointDefinition: endpointDefinition);
                return new EmptyResult(); // Response already written
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during proxy request: {EndpointName}", endpointName);

            return PortwayResults.ProblemWithTrace(context,
                $"Error processing endpoint {endpointName}. Please check the logs for more details.", "Error");
        }
    }

    /// <summary>Constructs the DELETE URL based on configured DeletePattern</summary>
    private string ConstructDeleteUrl(
        string baseUrl,
        string? id,
        string remainingPath,
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

    /// <summary>Computes a SHA-256 hash of the (buffered) request body and rewinds it, for use in QUERY cache keys</summary>
    private static async Task<string> ComputeBodyHashAsync(HttpContext context)
    {
        if (context.Request.Body == null)
        {
            return string.Empty;
        }
        if (context.Request.Body.CanSeek)
        {
            context.Request.Body.Position = 0;
        }
        using var ms = new MemoryStream();
        await context.Request.Body.CopyToAsync(ms);
        if (context.Request.Body.CanSeek)
        {
            context.Request.Body.Position = 0;
        }
        return Convert.ToHexString(SHA256.HashData(ms.ToArray()));
    }

    /// <summary>Creates a cache key based on request details</summary>
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

    /// <summary>Executes the actual proxy request and writes the response</summary>
    private async Task<(bool IsSuccessful, string Content, Dictionary<string, string> Headers, int StatusCode, string? ContentType)> ExecuteProxyRequest(
        HttpContext context,
        string method, string fullUrl, string env, 
        ProxyEndpointInfo endpointConfig,
        string endpointName,
        bool isSoapRequest = false,
        string? originalMethod = null,
        EndpointDefinition? endpointDefinition = null)
    {
        // Create HttpClient
        var client = _httpClientFactory.CreateClient("ProxyClient");

        // Log the actual HTTP method being sent to backend
        Log.Debug("Sending {Method} request to backend: {Url}", method, fullUrl);

        // Buffer request body once so it can be re-sent on retry or failover
        byte[]? bodyBytes = null;
        if (HttpMethods.IsPost(method) ||
            HttpMethods.IsPut(method) ||
            HttpMethods.IsPatch(method) ||
            HttpMethods.IsDelete(method) ||
            method.Equals("MERGE", StringComparison.OrdinalIgnoreCase))
        {
            context.Request.EnableBuffering();
            using var memoryStream = new MemoryStream();
            await context.Request.Body.CopyToAsync(memoryStream);
            context.Request.Body.Position = 0;
            bodyBytes = memoryStream.ToArray();
        }

        // Strip client-supplied headers that could enable IP spoofing or HTTP desync attacks; X-Forwarded-* headers are rebuilt from the verified connection IP; Transfer-Encoding and Content-Length are recalculated by HttpClient after body buffering
        var headersToStrip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Hop-by-hop headers (RFC 2616)
            "Host", "Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization",
            "TE", "Trailers", "Transfer-Encoding", "Upgrade",

            // Client-supplied forwarding headers (IP spoofing risk)
            "X-Forwarded-For", "X-Forwarded-Host", "X-Forwarded-Proto", "X-Forwarded-Port",
            "X-Real-IP", "X-Original-For", "Forwarded",

            // Content-Length - we buffer and recalculate to prevent desync
            "Content-Length"
        };

        // Load environment settings
        var (_, _, envHeaders) = await _environmentSettingsProvider.LoadEnvironmentOrThrowAsync(env);

        HttpRequestMessage BuildRequest(string targetUrl)
        {
            var requestMessage = new HttpRequestMessage(new HttpMethod(method), targetUrl);

            if (bodyBytes != null)
            {
                requestMessage.Content = new ByteArrayContent(bodyBytes);
                if (context.Request.ContentType != null)
                {
                    // Parse, not the ctor: values like "application/json; charset=utf-8" carry
                    // parameters the ctor rejects with a FormatException (500 on the request)
                    requestMessage.Content.Headers.ContentType =
                        System.Net.Http.Headers.MediaTypeHeaderValue.Parse(context.Request.ContentType);
                }
            }

            foreach (var header in context.Request.Headers)
            {
                if (headersToStrip.Contains(header.Key))
                    continue;

                try
                {
                    if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // SOAPAction must be enclosed in quotes for SOAP 1.1
                    if (isSoapRequest && header.Key.Equals("SOAPAction", StringComparison.OrdinalIgnoreCase))
                    {
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

            // Re-add a clean X-Forwarded-For from the verified connection IP
            var clientIp = context.Connection.RemoteIpAddress?.ToString();
            if (!string.IsNullOrEmpty(clientIp))
                requestMessage.Headers.TryAddWithoutValidation("X-Forwarded-For", clientIp);

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

            // Custom headers from HttpMethodAppendHeaders custom property
            if (endpointDefinition != null && !string.IsNullOrEmpty(originalMethod))
            {
                var existingHeaders = new List<string>();
                foreach (var header in context.Request.Headers)
                    existingHeaders.Add(header.Key);
                foreach (var header in envHeaders)
                    existingHeaders.Add(header.Key);

                var customHeaders = PortwayApi.Helpers.HttpMethodHeaderAppender.GetAppendHeaders(
                    originalMethod, method, endpointDefinition.CustomProperties,
                    existingHeaders, PortwayApi.Helpers.HeaderConflictResolution.Skip);

                foreach (var header in customHeaders)
                {
                    try
                    {
                        if (PortwayApi.Helpers.HttpMethodHeaderAppender.IsValidHeaderName(header.Key))
                        {
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

            return requestMessage;
        }

        var response = await PortwayApi.Helpers.ProxyFailoverHelper.SendWithRetryAsync(
            client, BuildRequest, fullUrl, endpointConfig.Url, endpointDefinition?.FallbackUrls,
            endpointDefinition?.Retry, $"endpoint '{endpointName}'", context.RequestAborted);

        // Store response headers for cache and apply to current response
        var responseHeaders = new Dictionary<string, string>();

        // Copy response headers
        foreach (var header in response.Headers)
        {
            if (!header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Headers[header.Key] = header.Value.ToArray();
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
                    context.Response.Headers[header.Key] = header.Value.ToArray();
                    responseHeaders[header.Key] = string.Join(",", header.Value);
                }
            }
        }
        
        // For GET requests, ensure Cache-Control header is set (except for SOAP)
        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase) && !isSoapRequest && !responseHeaders.ContainsKey("Cache-Control"))
        {
            // Add a default cache control header
            context.Response.Headers["Cache-Control"] = "public, max-age=300"; // 5 minutes
            responseHeaders["Cache-Control"] = "public, max-age=300";
        }

        // Set status code
        context.Response.StatusCode = (int)response.StatusCode;

        // Read and potentially rewrite response content
        var originalContent = response.Content != null
            ? await response.Content.ReadAsStringAsync(context.RequestAborted)
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
            if (!context.Response.Headers.ContainsKey("Content-Type"))
            {
                if (originalContent.Contains("<soap:Envelope") || originalContent.Contains("<SOAP-ENV:Envelope"))
                {
                    context.Response.Headers["Content-Type"] = "text/xml; charset=utf-8";
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
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync("Error processing request");
                return (false, string.Empty, responseHeaders, 500, null);
            }

            var originalHost = $"{originalUri.Scheme}://{originalUri.Host}:{originalUri.Port}";
            var originalPath = originalUri.AbsolutePath.TrimEnd('/');

            // Proxy path = /api/{env}/{endpoint}
            var proxyHost = $"{context.Request.Scheme}://{context.Request.Host}";
            var proxyPath = $"/api/{env}/{endpointName}";

            // Apply URL rewriting
            rewrittenContent = UrlRewriter.RewriteUrl(
                originalContent,
                originalHost,
                originalPath,
                proxyHost,
                proxyPath);

            // Apply declarative response transforms on JSON payloads; runs before caching so cached entries are shaped
            if (endpointDefinition?.ResponseTransforms is { HasRules: true } transforms &&
                contentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
            {
                rewrittenContent = ResponseTransformHelper.Apply(rewrittenContent, transforms);
            }
        }

        // Write the content to the response
        await context.Response.WriteAsync(rewrittenContent);

        Log.Debug("Proxy request completed: {Method} {Path} -> {StatusCode}", 
            method, context.Request.Path, response.StatusCode);
            
        return (
            response.IsSuccessStatusCode, 
            rewrittenContent, 
            responseHeaders, 
            (int)response.StatusCode,
            contentType
        );
    }


    /// <summary>Gets the DELETE pattern for a proxy endpoint (with default fallback)</summary>
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
}
