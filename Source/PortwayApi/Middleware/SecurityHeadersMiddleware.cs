namespace PortwayApi.Middleware;

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

/// <summary>
/// Per-request nonce for the inline scripts the server emits; the Content-Security-Policy runs no other inline script
/// </summary>
public static partial class CspNonce
{
    private const string ItemKey = "portway.csp-nonce";

    public static string For(HttpContext context) =>
        (string)(context.Items[ItemKey] ??= Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)));

    /// <summary>Adds the request nonce to every script element without a src attribute</summary>
    public static string Apply(string html, HttpContext context) =>
        InlineScript().Replace(html, $"<script nonce=\"{For(context)}\"");

    [GeneratedRegex(@"<script(?![^>]*\ssrc=)")]
    private static partial Regex InlineScript();
}

public sealed class SecurityHeadersMiddleware
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
            if (!context.Response.Headers.ContainsKey("Content-Security-Policy"))
                context.Response.Headers["Content-Security-Policy"] = ContentSecurityPolicy(CspNonce.For(context));

            // Console-only isolation: same-origin here would break cross-origin consumers of the gateway itself
            if (context.Request.Path.StartsWithSegments("/_"))
            {
                context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
                context.Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
            }

            // HSTS over HTTPS only
            if (context.Request.IsHttps)
            {
                context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
            }

            return Task.CompletedTask;
        });

        await _next(context);
    }

    // Inline scripts run only with the request nonce; inline event handlers never run
    private static string ContentSecurityPolicy(string nonce) =>
        "default-src 'self'; " +
        $"script-src 'self' 'nonce-{nonce}'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; " +
        "connect-src 'self'; " +
        "font-src 'self'; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "frame-ancestors 'none'; " +
        "form-action 'none'";

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
        return path.StartsWithSegments("/static") ||
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