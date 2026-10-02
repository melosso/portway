namespace PortwayApi.Middleware;

using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using PortwayApi.Helpers;
using Serilog;

/// <summary>
/// Configures forwarded headers and Cloudflare IP/scheme restoration.
/// </summary>
public static class ForwardedHeadersExtensions
{
    public static WebApplication UseProxyForwardedHeaders(this WebApplication app)
    {
        var forwardedHeadersOptions = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor |
                            ForwardedHeaders.XForwardedProto |
                            ForwardedHeaders.XForwardedHost,

            // avoids iis header symmetry warnings
            RequireHeaderSymmetry = false,
            ForwardLimit = null
        };
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
        {
            Log.Warning("ForwardedHeaders: no trusted proxies configured, X-Forwarded-For ignored. Set ForwardedHeaders:KnownProxies.");
        }
        else
        {
            Log.Information("ForwardedHeaders: Trusting {ProxyCount} proxy IP(s) and {NetworkCount} network(s)", forwardedHeadersOptions.KnownProxies.Count, forwardedHeadersOptions.KnownIPNetworks.Count);
            app.UseForwardedHeaders(forwardedHeadersOptions);
        }

        // restore client ip and scheme for requests from cloudflare
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