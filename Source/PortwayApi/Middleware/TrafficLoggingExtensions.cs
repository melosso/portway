namespace PortwayApi.Middleware;

using System;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Serilog;

/// <summary>
/// Extension methods for setting up proxy traffic logging
/// </summary>
public static class TrafficLoggingExtensions
{
    /// <summary>
    /// Adds proxy traffic logging services to the service collection
    /// </summary>
    public static IServiceCollection AddRequestTrafficLogging(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind options from configuration
        var optionsSection = configuration.GetSection("RequestTrafficLogging");
        services.Configure<ProxyTrafficLoggerOptions>(optionsSection);

        // Get options for setup
        var options = optionsSection.Get<ProxyTrafficLoggerOptions>() ?? new ProxyTrafficLoggerOptions();

        // Only register services if enabled
        if (options.Enabled)
        {
            Log.Debug("Traffic logging will be initialized, using {StorageType} storage", options.StorageType);

            // Create bounded channel with specified capacity
            services.AddSingleton(_ => Channel.CreateBounded<ProxyTrafficLogEntry>(
                new BoundedChannelOptions(options.QueueCapacity)
                {
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleReader = true,
                    SingleWriter = false
                }));

            // Register the appropriate storage implementation
            if (string.Equals(options.StorageType, "sqlite", StringComparison.OrdinalIgnoreCase))
            {
                services.AddSingleton<ITrafficLogStorage, SqliteTrafficLogStorage>();
            }
            else
            {
                services.AddSingleton<ITrafficLogStorage, FileTrafficLogStorage>();
            }

            // Register the background service
            services.AddHostedService<ProxyTrafficLoggerService>();
        }

        return services;
    }

    /// <summary>
    /// Adds the proxy traffic logging middleware to the application pipeline
    /// </summary>
    public static IApplicationBuilder UseRequestTrafficLogging(this IApplicationBuilder app)
    {
        var options = app.ApplicationServices.GetService<IOptions<ProxyTrafficLoggerOptions>>();

        // Only use middleware if enabled
        if (options?.Value.Enabled == true)
        {
            // Check if the Channel service is registered
            var channel = app.ApplicationServices.GetService<Channel<ProxyTrafficLogEntry>>();
            if (channel != null)
            {
                app.UseMiddleware<ProxyTrafficLoggerMiddleware>();
                Log.Debug("Proxy traffic logging middleware enabled");
            }
            else
            {
                Log.Debug("Proxy traffic logging middleware not enabled because required services are not registered");
            }
        }

        return app;
    }
}
