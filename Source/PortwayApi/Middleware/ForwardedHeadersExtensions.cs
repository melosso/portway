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

        // Trust X-Forwarded-For only from explicitly configured proxies; empty means the header is ignored and RemoteIpAddress stays the real TCP peer
        forwardedHeadersOptions.KnownIPNetworks.Clear();
        forwardedHeadersOptions.KnownProxies.Clear();

        var knownProxies = app.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
        var knownNetworks = app.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [];

        foreach (var proxy in knownProxies)
            if (IPAddress.TryParse(proxy, out var ip))
                forwardedHeadersOptions.KnownProxies.Add(ip);

        foreach (var network in knownNetworks)
            if (System.Net.IPNetwork.TryParse(network, out var net))
                forwardedHeadersOptions.KnownIPNetworks.Add(net);

        if (forwardedHeadersOptions.KnownProxies.Count == 0 && forwardedHeadersOptions.KnownIPNetworks.Count == 0)
            Log.Warning("ForwardedHeaders: no trusted proxies configured; X-Forwarded-For is ignored. Behind a reverse proxy, client IPs will be the proxy IP, which weakens per-IP rate limiting and the Web UI network gate. Set ForwardedHeaders:KnownProxies to fix.");
        else
            Log.Information("ForwardedHeaders: trusting {ProxyCount} proxy IP(s) and {NetworkCount} network(s) for X-Forwarded-For", forwardedHeadersOptions.KnownProxies.Count, forwardedHeadersOptions.KnownIPNetworks.Count);

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
