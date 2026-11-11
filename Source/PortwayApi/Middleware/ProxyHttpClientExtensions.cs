using System.Net;
using Serilog;
using PortwayApi.Helpers;

namespace PortwayApi.Middleware;

public static class ProxyHttpClientExtensions
{
    public const string Client = "ProxyClient";
    public const string NoRedirectClient = "ProxyClientNoRedirect";

    public static IServiceCollection AddPortwayProxyHttpClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var proxyUsername = EnvAliases.GetDirect("PORTWAY_PROXY_USERNAME")
            ?? configuration["Proxy:Username"];
        var proxyPassword = EnvAliases.GetDirect("PORTWAY_PROXY_PASSWORD")
            ?? configuration["Proxy:Password"];
        var proxyDomain = EnvAliases.GetDirect("PORTWAY_PROXY_DOMAIN")
            ?? configuration["Proxy:Domain"];

        Log.Debug("Proxy credentials found: username={HasUsername}, password={HasPassword}, domain={HasDomain}",
            !string.IsNullOrEmpty(proxyUsername),
            !string.IsNullOrEmpty(proxyPassword),
            !string.IsNullOrEmpty(proxyDomain));

        var useCredentials = !string.IsNullOrEmpty(proxyUsername) && !string.IsNullOrEmpty(proxyPassword);
        if (useCredentials)
            Log.Information("Configuring proxy with explicit credentials for user: {Username}@{Domain}", proxyUsername, proxyDomain);

        foreach (var (name, allowRedirect) in new[] { (Client, true), (NoRedirectClient, false) })
        {
            services.AddHttpClient(name)
                .ConfigurePrimaryHttpMessageHandler(sp => new SocketsHttpHandler
                {
                    UseProxy = useCredentials,
                    Credentials = useCredentials ? new NetworkCredential(proxyUsername, proxyPassword, proxyDomain) : CredentialCache.DefaultNetworkCredentials,
                    PreAuthenticate = true,
                    AllowAutoRedirect = allowRedirect,
                    PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                    ConnectCallback = sp.GetRequiredService<UrlValidator>().ConnectAsync
                });
        }

        return services;
    }
}
