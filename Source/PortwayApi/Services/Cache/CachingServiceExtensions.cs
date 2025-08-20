using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PortwayApi.Services.Caching;
using PortwayApi.Services.Health;
using Serilog;

namespace PortwayApi.Services.Caching;

/// <summary>
/// Extensions for registering caching services
/// </summary>
public static class CachingServiceExtensions
{
    /// <summary>
    /// Adds caching services to the application
    /// </summary>
    public static IServiceCollection AddCachingServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Load configuration
        var cacheOptions = new CacheOptions();
        configuration.GetSection("Caching").Bind(cacheOptions);

        // Register options
        services.Configure<CacheOptions>(configuration.GetSection("Caching"));

        // Register the memory cache provider (always needed, even as fallback)
        services.AddMemoryCache();
        services.AddSingleton<MemoryCacheProvider>();

        // Add Redis if configured
        if (cacheOptions.ProviderType == CacheProviderType.Redis)
        {
            Log.Information("🔧 Configuring Redis cache provider with connection to {ConnectionString}", 
                cacheOptions.Redis.ConnectionString);
            
            // Add StackExchange.Redis ConnectionMultiplexer
            services.AddSingleton<RedisCacheProvider>();

            // Add Redis health check
            services.AddHealthChecks()
                .AddCheck<RedisCacheHealthCheck>(
                    "redis_cache", 
                    HealthStatus.Degraded, 
                    new[] { "redis", "cache", "readiness" });
        }
        else
        {
            Log.Information("🔧 Configuring Memory cache provider");
        }

        // Add the cache manager
        services.AddSingleton<CacheManager>();

        return services;
    }
}
