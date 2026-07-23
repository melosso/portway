namespace PortwayApi.Services.Configuration;

using System.IO.Compression;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Server.Kestrel.Core;

/// <summary>Kestrel hardening, HTTPS opt-in detection and response compression for the Portway web host</summary>
public static class WebHostConfigurationExtensions
{
    public static WebApplicationBuilder ConfigurePortwayWebHost(this WebApplicationBuilder builder)
    {
        // In Docker HTTPS is opt-in (Use_HTTPS=true); on Windows Server/IIS it is on by default unless Use_HTTPS=false
        var useHttpsEnv = Environment.GetEnvironmentVariable("Use_HTTPS");
        var runningInContainer = string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase);
        bool useHttps;
        if (runningInContainer)
        {
            // In Docker, only enable HTTPS if explicitly requested
            useHttps = string.Equals(useHttpsEnv, "true", StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            // On Windows Server/IIS, enable HTTPS unless explicitly disabled
            useHttps = !string.Equals(useHttpsEnv, "false", StringComparison.OrdinalIgnoreCase);
        }

        builder.WebHost.ConfigureKestrel(serverOptions =>
        {
            // 1. Disable server header (security)
            serverOptions.AddServerHeader = false;

            // 2. Set appropriate request limits
            serverOptions.Limits.MaxRequestBodySize = 10 * 1024 * 1024; // 10 MB request body limit
            serverOptions.Limits.MaxRequestHeadersTotalSize = 32 * 1024; // 32 KB for headers

            // 3. Configure timeouts for better client handling
            serverOptions.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
            serverOptions.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);

            // 4. Connection rate limiting to prevent DoS
            serverOptions.Limits.MaxConcurrentConnections = 1000;
            serverOptions.Limits.MaxConcurrentUpgradedConnections = 100;

            // 5. Data rate limiting to prevent slow requests
            serverOptions.Limits.MinRequestBodyDataRate = new MinDataRate(
                bytesPerSecond: 100, gracePeriod: TimeSpan.FromSeconds(10));
            serverOptions.Limits.MinResponseDataRate = new MinDataRate(
                bytesPerSecond: 100, gracePeriod: TimeSpan.FromSeconds(10));

            // 6. HTTP/2 specific settings
            serverOptions.Limits.Http2.MaxStreamsPerConnection = 100;
            serverOptions.Limits.Http2.MaxFrameSize = 16 * 1024; // 16 KB
            serverOptions.Limits.Http2.InitialConnectionWindowSize = 128 * 1024; // 128 KB
            serverOptions.Limits.Http2.KeepAlivePingDelay = TimeSpan.FromSeconds(30);
            serverOptions.Limits.Http2.KeepAlivePingTimeout = TimeSpan.FromSeconds(60);

            // 7. Configure HTTPS only if Use_HTTPS is set to 'true' (opt-in)
            if (!builder.Environment.IsDevelopment() && useHttps)
            {
                serverOptions.ConfigureEndpointDefaults(listenOptions =>
                {
                    listenOptions.UseHttps();
                });
            }
        });

        builder.Services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
        });

        builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
        {
            options.Level = CompressionLevel.Fastest;
        });
        builder.Services.Configure<GzipCompressionProviderOptions>(options =>
        {
            options.Level = CompressionLevel.Fastest;
        });

        return builder;
    }
}
