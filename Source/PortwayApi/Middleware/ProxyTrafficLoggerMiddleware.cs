namespace PortwayApi.Middleware;

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PortwayApi.Auth;
using Serilog;

/// <summary>Middleware for logging proxy traffic</summary>
public class ProxyTrafficLoggerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ProxyTrafficLoggerOptions _options;
    private readonly System.Threading.Channels.Channel<ProxyTrafficLogEntry> _logChannel;
    private readonly IServiceProvider _serviceProvider;
    
    // Pre-define list of sensitive headers - using static readonly for better performance
    private static readonly string[] _sensitiveHeaders = new[]
    {
        "Authorization", "Cookie", "X-API-Key", "API-Key", "Password",
        "X-Auth-Token", "Token", "Secret", "Credential", "Access-Token", 
        "X-Access-Token"
    };

    public ProxyTrafficLoggerMiddleware(
        RequestDelegate next, 
        IOptions<ProxyTrafficLoggerOptions> options,
        System.Threading.Channels.Channel<ProxyTrafficLogEntry> logChannel,
        IServiceProvider serviceProvider)
    {
        _next = next;
        _options = options.Value;
        _logChannel = logChannel;
        _serviceProvider = serviceProvider;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip logging if not enabled or if this is a non-API request
        if (!_options.Enabled || !IsApiRequest(context))
        {
            await _next(context);
            return;
        }

        var traceId = Guid.NewGuid().ToString("N").Substring(0, 8);

        // Prepare log entry
        var logEntry = new ProxyTrafficLogEntry
        {
            Timestamp = DateTime.UtcNow,
            Method = context.Request.Method,
            Path = context.Request.Path.Value ?? string.Empty,
            QueryString = context.Request.QueryString.Value ?? string.Empty,
            ClientIp = GetClientIpAddress(context),
            TraceId = traceId
        };

        // Extract environment and endpoint from path
        ParseApiPath(context.Request.Path.Value, out string? env, out string? endpoint);
        logEntry.Environment = env ?? string.Empty;
        logEntry.EndpointName = endpoint ?? string.Empty;
        
        // Log at debug level only
        Serilog.Log.Debug($"[Trace: {traceId}] Processing {context.Request.Method} request to {context.Request.Path}");

        // Extract target URL if available in Items
        if (context.Items.TryGetValue("TargetUrl", out var targetUrl) && targetUrl != null)
        {
            logEntry.TargetUrl = targetUrl.ToString() ?? string.Empty;
        }
        
        // Capture request headers if enabled
        if (_options.CaptureHeaders)
        {
            CaptureRequestHeaders(context, logEntry);
        }

        // Setup for request body capture
        var originalRequestBody = context.Request.Body;
        MemoryStream? requestBodyStream = null;

        // Setup for response capture - only do this if needed
        Stream? originalResponseBodyStream = null;
        MemoryStream? responseBodyStream = null;
        
        if (_options.IncludeResponseBodies)
        {
            originalResponseBodyStream = context.Response.Body;
            responseBodyStream = new MemoryStream();
            context.Response.Body = responseBodyStream;
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Capture request body if enabled
            if (_options.IncludeRequestBodies)
            {
                // Create a new MemoryStream to capture the request body
                requestBodyStream = new MemoryStream();
                
                // Enable buffering to allow multiple reads
                context.Request.EnableBuffering();
                
                // Copy the original request body to our memory stream
                await context.Request.Body.CopyToAsync(requestBodyStream);
                
                // Reset the memory stream position to the beginning
                requestBodyStream.Position = 0;
                
                // Read the request body for logging
                using (var reader = new StreamReader(
                    requestBodyStream, 
                    Encoding.UTF8, 
                    detectEncodingFromByteOrderMarks: false, 
                    leaveOpen: true))
                {
                    var requestBody = await reader.ReadToEndAsync();
                    
                    // Truncate the body if it's too large
                    if (requestBody.Length > _options.MaxBodyCaptureSizeBytes)
                    {
                        logEntry.RequestBody = requestBody.Substring(0, _options.MaxBodyCaptureSizeBytes) + "...";
                    }
                    else
                    {
                        logEntry.RequestBody = requestBody;
                    }
                }
                
                // Record the size
                logEntry.RequestSize = requestBodyStream.Length;
                
                // Reset the position for the next middleware
                requestBodyStream.Position = 0;
                
                // Reset the original request body position
                context.Request.Body.Position = 0;
            }
            else
            {
                // Just record content length
                logEntry.RequestSize = context.Request.ContentLength ?? 0;
            }

            // Extract username from the token in the Authorization header
            await ExtractUsernameFromTokenAsync(context, logEntry);

            // Call the next middleware in the pipeline
            await _next(context);

            // Record duration
            stopwatch.Stop();
            logEntry.DurationMs = (int)stopwatch.ElapsedMilliseconds;
            
            // Capture response details
            logEntry.StatusCode = context.Response.StatusCode;
            
            // Capture response body if enabled
            if (_options.IncludeResponseBodies && responseBodyStream != null && originalResponseBodyStream != null)
            {
                responseBodyStream.Position = 0;
                logEntry.ResponseSize = responseBodyStream.Length;
                
                // Only capture for specific content types
                var contentType = context.Response.ContentType?.ToLowerInvariant() ?? string.Empty;
                if ((contentType.Contains("json") || contentType.Contains("xml")) && responseBodyStream.Length > 0)
                {
                    // Read the response body for logging
                    using (var reader = new StreamReader(
                        responseBodyStream, 
                        Encoding.UTF8, 
                        detectEncodingFromByteOrderMarks: false, 
                        leaveOpen: true))
                    {
                        var responseBody = await reader.ReadToEndAsync();
                        
                        // Truncate the body if it's too large
                        if (responseBody.Length > _options.MaxBodyCaptureSizeBytes)
                        {
                            logEntry.ResponseBody = responseBody.Substring(0, _options.MaxBodyCaptureSizeBytes) + "...";
                        }
                        else
                        {
                            logEntry.ResponseBody = responseBody;
                        }
                    }
                    
                    // Reset the position for copying to the original stream
                    responseBodyStream.Position = 0;
                }
                
                // Copy the captured response to the original stream
                await responseBodyStream.CopyToAsync(originalResponseBodyStream);
            }
            else if (responseBodyStream != null && originalResponseBodyStream != null)
            {
                // Just record the size
                logEntry.ResponseSize = responseBodyStream.Length;
                
                // Copy the captured response to the original stream
                responseBodyStream.Position = 0;
                await responseBodyStream.CopyToAsync(originalResponseBodyStream);
            }
            
            // Log with Serilog for immediate visibility
            if (_options.EnableInfoLogging)
            {
                Serilog.Log.Information($"[Trace: {traceId}] {logEntry.Method} {logEntry.Path} -> {logEntry.StatusCode} ({logEntry.DurationMs}ms)");
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logEntry.DurationMs = (int)stopwatch.ElapsedMilliseconds;
            logEntry.StatusCode = 500;  // Internal Server Error
            
            Serilog.Log.Error(ex, $"[Trace: {traceId}] Error during proxy request processing");
            
            // Re-throw the exception
            throw;
        }
        finally
        {
            // Restore the original request body if we changed it
            if (requestBodyStream != null)
            {
                context.Request.Body = originalRequestBody;
                await requestBodyStream.DisposeAsync();
            }
            
            // Restore the original response body stream if we changed it
            if (originalResponseBodyStream != null && responseBodyStream != null)
            {
                context.Response.Body = originalResponseBodyStream;
                await responseBodyStream.DisposeAsync();
            }
            
            // Try to add the log entry to the channel
            if (!_logChannel.Writer.TryWrite(logEntry))
            {
                Serilog.Log.Warning($"[Trace: {traceId}] Failed to write traffic log entry to channel - queue might be full");
            }
        }
    }

    private bool IsApiRequest(HttpContext context)
    {
        var path = context.Request.Path.Value;
        return path != null && 
                (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/webhook/", StringComparison.OrdinalIgnoreCase)) && 
                !path.Contains("/docs", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains("index.html", StringComparison.OrdinalIgnoreCase);
    }
    
    private void ParseApiPath(string? path, out string? env, out string? endpoint)
    {
        env = null;
        endpoint = null;
        
        if (string.IsNullOrEmpty(path))
            return;
        
        // Extract segments
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        
        if (segments.Length >= 2 && 
            (segments[0].Equals("api", StringComparison.OrdinalIgnoreCase) || 
            segments[0].Equals("webhook", StringComparison.OrdinalIgnoreCase)))
        {
            // Set environment
            env = segments[1];
            
            // Set endpoint if available
            if (segments.Length >= 3)
            {
                endpoint = segments[2];
                
                // Handle composite endpoints
                if (endpoint.Equals("composite", StringComparison.OrdinalIgnoreCase) && segments.Length >= 4)
                {
                    endpoint = $"composite/{segments[3]}";
                }
            }
        }
    }
    
    private async Task ExtractUsernameFromTokenAsync(HttpContext context, ProxyTrafficLogEntry logEntry)
    {
        try
        {
            // Try to get from User Identity first
            logEntry.Username = context.User?.Identity?.Name;
            
            // If not available, check Authorization header
            if (string.IsNullOrEmpty(logEntry.Username) && 
                context.Request.Headers.TryGetValue("Authorization", out var authHeader))
            {
                string token = authHeader.ToString();
                if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    // Extract the actual token value
                    token = token["Bearer ".Length..].Trim();
                    
                    // Use the token service to get the username for this token
                    using var scope = _serviceProvider.CreateScope();
                    var tokenService = scope.ServiceProvider.GetService<Auth.TokenService>();
                    
                    if (tokenService != null)
                    {
                        // Get active tokens
                        var tokens = await tokenService.GetActiveTokensAsync();
                        
                        // Check each token - we need to use VerifyTokenAsync because the token is hashed
                        foreach (var activeToken in tokens)
                        {
                            // Verify if this token belongs to this user
                            bool isValid = await tokenService.VerifyTokenAsync(token, activeToken.Username);
                            if (isValid)
                            {
                                logEntry.Username = activeToken.Username;
                                break;
                            }
                        }
                        
                        // If we couldn't find a username but the token is valid, use a generic name
                        if (string.IsNullOrEmpty(logEntry.Username) && await tokenService.VerifyTokenAsync(token))
                        {
                            logEntry.Username = "authenticated-user";
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, $"[Trace: {logEntry.TraceId}] Error extracting username from token");
            logEntry.Username = "error-extracting-user";
        }
    }

    private void CaptureRequestHeaders(HttpContext context, ProxyTrafficLogEntry logEntry)
    {
        try
        {
            var headers = context.Request.Headers;
            
            // Pre-allocate dictionary capacity
            logEntry.RequestHeaders = new Dictionary<string, string>(headers.Count, StringComparer.OrdinalIgnoreCase);
            
            foreach (var header in headers)
            {
                string headerName = header.Key;
                string headerValue = header.Value.ToString();
                
                // Check if this is a sensitive header
                bool isSensitive = false;
                foreach (var sensitiveHeader in _sensitiveHeaders)
                {
                    if (string.Equals(headerName, sensitiveHeader, StringComparison.OrdinalIgnoreCase))
                    {
                        isSensitive = true;
                        break;
                    }
                }
                
                // Add to dictionary with appropriate value
                logEntry.RequestHeaders[headerName] = isSensitive ? "[REDACTED]" : headerValue;
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, $"[Trace: {logEntry.TraceId}] Error capturing request headers");
        }
    }

    private string GetClientIpAddress(HttpContext context)
    {
        // Try to get the forwarded IP first
        string? ip = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        
        // If not available, use the connection remote IP
        if (string.IsNullOrEmpty(ip))
        {
            ip = context.Connection.RemoteIpAddress?.ToString();
        }
        else
        {
            ip = ip.Split(',').FirstOrDefault()?.Trim() ?? "unknown";
        }
        
        return ip ?? "unknown";
    }
}
