using System.Net;
using Serilog;

namespace PortwayApi.Middleware;

public static class ProxyHttpClientExtensions
{
    public static IServiceCollection AddPortwayProxyHttpClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var proxyUsername = Environment.GetEnvironmentVariable("PROXY_USERNAME")
            ?? configuration["Proxy:Username"];
        var proxyPassword = Environment.GetEnvironmentVariable("PROXY_PASSWORD")
            ?? configuration["Proxy:Password"];
        var proxyDomain = Environment.GetEnvironmentVariable("PROXY_DOMAIN")
            ?? configuration["Proxy:Domain"];

        Log.Debug("Proxy credentials found: username={HasUsername}, password={HasPassword}, domain={HasDomain}",
            !string.IsNullOrEmpty(proxyUsername),
            !string.IsNullOrEmpty(proxyPassword),
            !string.IsNullOrEmpty(proxyDomain));

        if (!string.IsNullOrEmpty(proxyUsername) && !string.IsNullOrEmpty(proxyPassword))
        {
            Log.Information("Configuring proxy with explicit credentials for user: {Username}@{Domain}",
                proxyUsername, proxyDomain);
            services.AddHttpClient("ProxyClient")
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    Credentials = new NetworkCredential(proxyUsername, proxyPassword, proxyDomain),
                    PreAuthenticate = true,
                    PooledConnectionLifetime = TimeSpan.FromMinutes(10)
                });
        }
        else
        {
            services.AddHttpClient("ProxyClient")
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    UseProxy = false,
                    Credentials = CredentialCache.DefaultNetworkCredentials,
                    PreAuthenticate = true,
                    PooledConnectionLifetime = TimeSpan.FromMinutes(10)
                });
        }

        return services;
    }
}
