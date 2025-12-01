namespace PortwayApi.Middleware;

using Microsoft.AspNetCore.Http;
using Serilog;

/// <summary>
/// Middleware for handling content negotiation and request validation.
/// Ensures consistent Content-Type validation on requests and proper Accept header handling.
/// </summary>
public class ContentNegotiationMiddleware
{
    private readonly RequestDelegate _next;
    
    // Paths that should skip JSON content-type validation (e.g., file uploads, proxy passthrough)
    private static readonly HashSet<string> _skipContentTypeValidationPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/files",
        "/health",
        "/docs",
        "/swagger"
    };
    
    // Paths that are passthrough and should not enforce content negotiation
    private static readonly string[] _passthroughIndicators = { "proxy", "Proxy" };
    
    // Maximum request body size (50MB)
    private const long MaxRequestBodySize = 52_428_800;

    public ContentNegotiationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        
        // Skip validation for certain paths
        if (ShouldSkipValidation(path))
        {
            await _next(context);
            return;
        }
        
        // Validate Content-Type for requests with body
        if (IsRequestWithBody(context.Request.Method))
        {
            var validationResult = ValidateRequestContentType(context);
            if (!validationResult.IsValid)
            {
                await WriteErrorResponse(context, validationResult.StatusCode, validationResult.Error, validationResult.Detail);
                return;
            }
        }
        
        // Set default response Content-Type if Accept header handling is needed
        context.Response.OnStarting(() =>
        {
            EnsureResponseContentType(context);
            return Task.CompletedTask;
        });

        await _next(context);
    }

    /// <summary>
    /// Determines if validation should be skipped for this path
    /// </summary>
    private bool ShouldSkipValidation(string path)
    {
        // Skip for file uploads, health checks, docs
        foreach (var skipPath in _skipContentTypeValidationPaths)
        {
            if (path.Contains(skipPath, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        
        // Skip for proxy endpoints (they handle their own content negotiation)
        foreach (var indicator in _passthroughIndicators)
        {
            if (path.Contains(indicator, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        
        return false;
    }

    /// <summary>
    /// Checks if the HTTP method typically includes a request body
    /// </summary>
    private static bool IsRequestWithBody(string method)
    {
        return HttpMethods.IsPost(method) || 
               HttpMethods.IsPut(method) || 
               HttpMethods.IsPatch(method);
    }

    /// <summary>
    /// Validates the Content-Type header for requests with body
    /// </summary>
    private (bool IsValid, int StatusCode, string Error, string Detail) ValidateRequestContentType(HttpContext context)
    {
        var contentType = context.Request.ContentType;
        var contentLength = context.Request.ContentLength;
        
        // Check content length first
        if (contentLength > MaxRequestBodySize)
        {
            Log.Warning("Request body too large: {ContentLength} bytes from {RemoteIp}", 
                contentLength, context.Connection.RemoteIpAddress);
            
            return (false, StatusCodes.Status413PayloadTooLarge, 
                "Payload Too Large", 
                $"Request body exceeds maximum size of {MaxRequestBodySize / 1024 / 1024}MB");
        }
        
        // Allow empty body without content-type (some clients don't send it)
        if (contentLength == 0 || contentLength == null)
        {
            return (true, 0, string.Empty, string.Empty);
        }
        
        // Validate content type for requests with body
        if (string.IsNullOrEmpty(contentType))
        {
            Log.Warning("Missing Content-Type header for {Method} request from {RemoteIp}", 
                context.Request.Method, context.Connection.RemoteIpAddress);
            
            return (false, StatusCodes.Status415UnsupportedMediaType,
                "Unsupported Media Type",
                "Content-Type header is required for requests with body. Use application/json.");
        }
        
        // Check for JSON content type
        if (!contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning("Invalid Content-Type '{ContentType}' for {Method} request from {RemoteIp}", 
                contentType, context.Request.Method, context.Connection.RemoteIpAddress);
            
            return (false, StatusCodes.Status415UnsupportedMediaType,
                "Unsupported Media Type",
                $"Content-Type '{contentType}' is not supported. Use application/json.");
        }
        
        return (true, 0, string.Empty, string.Empty);
    }

    /// <summary>
    /// Ensures the response has a Content-Type header set
    /// </summary>
    private static void EnsureResponseContentType(HttpContext context)
    {
        // Only set if not already set and response is successful
        if (string.IsNullOrEmpty(context.Response.ContentType) && 
            context.Response.StatusCode >= 200 && 
            context.Response.StatusCode < 300)
        {
            // Default to JSON for API responses
            var path = context.Request.Path.Value ?? string.Empty;
            if (path.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.ContentType = "application/json; charset=utf-8";
            }
        }
    }

    /// <summary>
    /// Writes a standardized error response
    /// </summary>
    private static async Task WriteErrorResponse(HttpContext context, int statusCode, string error, string detail)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        
        await context.Response.WriteAsJsonAsync(new
        {
            error,
            detail,
            success = false
        });
    }
}

/// <summary>
/// Extension methods for ContentNegotiationMiddleware registration
/// </summary>
public static class ContentNegotiationMiddlewareExtensions
{
    /// <summary>
    /// Adds content negotiation middleware to the pipeline.
    /// This validates Content-Type on POST/PUT/PATCH and ensures proper response headers.
    /// </summary>
    public static IApplicationBuilder UseContentNegotiation(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ContentNegotiationMiddleware>();
    }
}
