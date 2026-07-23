namespace PortwayApi.Middleware;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog;

/// <summary>Extension methods for Rate Limiting</summary>
public static class RateLimitingExtensions
{
    /// <summary>Adds rate limiting settings and the configured bucket store to the service collection</summary>
    public static IServiceCollection AddRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = new RateLimitSettings();
        configuration.GetSection("RateLimiting").Bind(settings);

        services.AddSingleton(settings);
        services.AddSingleton<RateLimiterState>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<InMemoryRateLimiterStore>();

        // Redis is opt-in only; memory remains the standard store
        bool useRedis = string.Equals(settings.Store, "Redis", StringComparison.OrdinalIgnoreCase);
        if (useRedis)
        {
            var connectionString = settings.RedisConnectionString;
            if (string.IsNullOrWhiteSpace(connectionString))
                connectionString = configuration.GetValue<string>("Caching:Redis:ConnectionString");

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                Log.Warning("RateLimiting:Store is Redis but no connection string is configured, using in-memory store");
                useRedis = false;
            }
            else
            {
                services.AddSingleton<IRateLimiterStore>(sp => new RedisRateLimiterStore(
                    connectionString,
                    sp.GetRequiredService<InMemoryRateLimiterStore>(),
                    sp.GetRequiredService<TimeProvider>()));
            }
        }

        if (!useRedis)
            services.AddSingleton<IRateLimiterStore>(sp => sp.GetRequiredService<InMemoryRateLimiterStore>());

        return services;
    }
}
