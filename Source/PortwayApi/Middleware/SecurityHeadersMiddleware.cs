namespace PortwayApi.Middleware;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Serilog;

public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly HashSet<string> _unsafeResponseHeaders;
    private readonly Dictionary<string, string> _securityHeaders;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
        
        // Headers to remove (prevent information disclosure)
        _unsafeResponseHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Server",
            "X-AspNet-Version",
            "X-SourceFiles",
            "X-AspNetMvc-Version"
        };
        
        // Carefully configured security headers
        _securityHeaders = new Dictionary<string, string>
        {
            // Custom branding
            { "X-Powered-By", "Portway API" },
            
            // Prevent MIME type sniffing
            { "X-Content-Type-Options", "nosniff" },
            
            // Prevent clickjacking completely
            { "X-Frame-Options", "DENY" },
            
            // Restrictive Content Security Policy
            { "Content-Security-Policy", 
                "default-src 'self'; " +
                "script-src 'self' 'unsafe-inline'; " +
                "style-src 'self' 'unsafe-inline'; " +
                "img-src 'self' data:; " +
                "connect-src 'self'; " +
                "font-src 'self'; " +
                "object-src 'none'; " +
                "base-uri 'self'; " +
                "form-action 'none'"
            },
            
            // Strict referrer policy
            { "Referrer-Policy", "strict-origin-when-cross-origin" },
            
            // Minimal, restrictive permissions policy
            { "Permissions-Policy", "geolocation=(), camera=(), microphone=(), payment=()" },
            
        };
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Inbound request validation
        if (!ValidateInboundRequest(context.Request))
        {
            context.Response.StatusCode = 400;
            return;
        }

        // Skip security headers for specific routes
        if (ShouldSkipSecurityHeaders(context.Request.Path))
        {
            await _next(context);
            return;
        }

        // Block cross-origin font requests
        if (IsFontRequest(context.Request.Path))
        {
            var origin = context.Request.Headers.Origin.ToString();
            if (!string.IsNullOrEmpty(origin))
            {
                var serverOrigin = $"{context.Request.Scheme}://{context.Request.Host}";
                if (!origin.Equals(serverOrigin, StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = 403;
                    return;
                }
                context.Response.Headers["Access-Control-Allow-Origin"] = origin;
            }
        }

        // Register a callback to modify headers after response generation
        context.Response.OnStarting(() =>
        {
            // Remove unsafe headers
            RemoveUnsafeHeaders(context.Response.Headers);

            // Add security headers
            AddSecurityHeaders(context.Response.Headers);

            // HSTS: only emit over HTTPS to prevent browsers caching it for HTTP-only deployments
            // (e.g. Docker behind a TLS-terminating reverse proxy with plain HTTP internally)
            if (context.Request.IsHttps)
            {
                context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
            }

            return Task.CompletedTask;
        });

        await _next(context);
    }

    private static bool IsFontRequest(PathString path)
    {
        var p = path.Value ?? "";
        return p.StartsWith("/fonts/", StringComparison.OrdinalIgnoreCase) &&
               (p.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase) ||
                p.EndsWith(".woff", StringComparison.OrdinalIgnoreCase) ||
                p.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ValidateInboundRequest(HttpRequest request)
    {
        var hasContentLength = request.Headers.ContainsKey("Content-Length");
        var hasTransferEncoding = request.Headers.ContainsKey("Transfer-Encoding");

        // Block conflicting Content-Length and Transfer-Encoding headers
        if (hasContentLength && hasTransferEncoding)
        {
            return false;
        }

        // Normalize Transfer-Encoding header value
        if (hasTransferEncoding)
        {
            var te = request.Headers.TransferEncoding.ToString();
            if (!te.Equals("chunked", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ShouldSkipSecurityHeaders(PathString path)
    {
        return path.StartsWithSegments("/openapi-docs") ||
               path.StartsWithSegments("/docs") ||
               path.StartsWithSegments("/static") ||
               path.StartsWithSegments("/index.html") ||
               path.StartsWithSegments("/health/live");
    }

    private void RemoveUnsafeHeaders(IHeaderDictionary headers)
    {
        foreach (var header in _unsafeResponseHeaders)
            headers.Remove(header);
    }

    private void AddSecurityHeaders(IHeaderDictionary headers)
    {
        foreach (var header in _securityHeaders)
        {
            if (!headers.ContainsKey(header.Key))
            {
                headers[header.Key] = header.Value;
            }
        }
    }
}

// Extension method to make middleware registration easy
public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<SecurityHeadersMiddleware>();
    }
}