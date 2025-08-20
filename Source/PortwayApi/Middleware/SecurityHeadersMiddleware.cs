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
            "X-Powered-By",
            "X-AspNet-Version",
            "X-SourceFiles",
            "X-AspNetMvc-Version"
        };
        
        // Carefully configured security headers
        _securityHeaders = new Dictionary<string, string>
        {
            // Prevent MIME type sniffing
            { "X-Content-Type-Options", "nosniff" },
            
            // Prevent clickjacking completely
            { "X-Frame-Options", "DENY" },
            
            // Restrictive Content Security Policy
            { "Content-Security-Policy", 
                "default-src 'self'; " +
                "script-src 'self'; " +
                "style-src 'self'; " +
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
            
            // HTTP Strict Transport Security (HSTS)
            { "Strict-Transport-Security", "max-age=31536000; includeSubDomains; preload" },
            
            // Disable old XSS filter, rely on CSP
            { "X-XSS-Protection", "0" }
        };
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip security headers for specific routes
        if (ShouldSkipSecurityHeaders(context.Request.Path))
        {
            await _next(context);
            return;
        }

        // Register a callback to modify headers after response generation
        context.Response.OnStarting(() =>
        {
            // Remove unsafe headers
            RemoveUnsafeHeaders(context.Response.Headers);
            
            // Add security headers
            AddSecurityHeaders(context.Response.Headers);
            
            return Task.CompletedTask;
        });

        await _next(context);
    }

    private bool ShouldSkipSecurityHeaders(PathString path)
    {
        return path.StartsWithSegments("/swagger") || 
               path.StartsWithSegments("/docs") ||
               path.StartsWithSegments("/static") ||
               path.StartsWithSegments("/index.html") ||
               path.StartsWithSegments("/health/live");
    }

    private void RemoveUnsafeHeaders(IHeaderDictionary headers)
    {
        foreach (var header in _unsafeResponseHeaders)
        {
            if (headers.ContainsKey(header))
            {
                headers.Remove(header);
            }
        }
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