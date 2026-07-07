namespace PortwayApi.Middleware;

using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using PortwayApi.Helpers;
using Serilog;

/// <summary>Forwarded-header handling for reverse proxies plus Cloudflare client IP and scheme restoration</summary>
public static class ForwardedHeadersExtensions
{
    public static WebApplication UsePortwayForwardedHeaders(this WebApplication app)
    {
        var forwardedHeadersOptions = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor |
                            ForwardedHeaders.XForwardedProto |
                            ForwardedHeaders.XForwardedHost,

            // Enforce header symmetry to prevent desync between front-end and back-end
            RequireHeaderSymmetry = true,

            // Support deep proxy chains
            ForwardLimit = null
        };

        // Configure known proxies based on environment
        var isProduction = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?.Equals("Production", StringComparison.OrdinalIgnoreCase) ?? false;

        if (!isProduction)
        {
            forwardedHeadersOptions.KnownIPNetworks.Clear();
            forwardedHeadersOptions.KnownProxies.Clear();
            Log.Warning("ForwardedHeaders: No known proxies configured. Add trusted proxies in production.");
        }
        else
        {
            // Production: cleared by default; configure via config file if needed
            forwardedHeadersOptions.KnownIPNetworks.Clear();
            forwardedHeadersOptions.KnownProxies.Clear();
        }

        app.UseForwardedHeaders(forwardedHeadersOptions);

        // Trust CF-Connecting-IP / CF-Visitor only when the TCP connection originates from a real Cloudflare IP; CF-Ray alone is not sufficient
        app.Use((context, next) =>
        {
            if (context.Request.Headers.TryGetValue("CF-Ray", out _) &&
                CloudflareIpRanges.IsCloudflareIp(context.Connection.RemoteIpAddress))
            {
                if (context.Request.Headers.TryGetValue("CF-Visitor", out var cfVisitor) &&
                    cfVisitor.ToString().Contains("\"scheme\":\"https\""))
                {
                    context.Request.Scheme = "https";
                }

                if (context.Request.Headers.TryGetValue("CF-Connecting-IP", out var cfIp) &&
                    IPAddress.TryParse(cfIp.ToString(), out var cfIpAddress))
                {
                    context.Connection.RemoteIpAddress = cfIpAddress;
                }
            }

            return next();
        });

        return app;
    }
}
