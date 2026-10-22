using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Serilog;

namespace PortwayApi.Services.Health;

/// <summary>
/// Health check for Redis cache
/// </summary>
public sealed class RedisCacheHealthCheck : IHealthCheck, IDisposable
{
    private readonly Caching.CacheOptions _options;
    private readonly ConnectionMultiplexer? _redis;
    private static readonly string HealthCheckKey = "health:check:key";

    public RedisCacheHealthCheck(IOptions<Caching.CacheOptions> options)
    {
        _options = options.Value;

        // Only try to connect if Redis is configured
        if (_options.ProviderType == Caching.CacheProviderType.Redis)
        {
            try
            {
                var configOptions = ConfigurationOptions.Parse(_options.Redis.ConnectionString);
                configOptions.AbortOnConnectFail = false;
                configOptions.ConnectTimeout = 2000; // 2 seconds timeout for health check
                configOptions.SyncTimeout = 2000;
                configOptions.Ssl = _options.Redis.UseSsl;

                _redis = ConnectionMultiplexer.Connect(configOptions);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to initialize Redis connection for health check");
            }
        }
    }

    /// <summary>
    /// Performs the health check
    /// </summary>
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        // If Redis is not configured, return healthy
        if (_options.ProviderType != Caching.CacheProviderType.Redis)
        {
            return HealthCheckResult.Healthy("Redis caching is not enabled");
        }

        try
        {
            if (_redis == null || !_redis.IsConnected)
            {
                return HealthCheckResult.Unhealthy("Redis connection is not established");
            }

            // Get a database instance
            var db = _redis.GetDatabase();

            // Test basic operations
            var testValue = $"health-check-{DateTime.UtcNow:yyyy-MM-dd-HH-mm-ss}";

            // Set a value
            if (!await db.StringSetAsync(
                $"{_options.Redis.InstanceName}{HealthCheckKey}",
                testValue,
                TimeSpan.FromSeconds(30)))
            {
                return HealthCheckResult.Degraded("Failed to set a value in Redis");
            }

            // Get the value back
            var retrievedValue = await db.StringGetAsync($"{_options.Redis.InstanceName}{HealthCheckKey}");

            if (!retrievedValue.HasValue || retrievedValue.ToString() != testValue)
            {
                return HealthCheckResult.Degraded("Redis set/get operations not working correctly");
            }

            // info requires allowadmin in stackexchange redis
            var latency = await db.PingAsync();

            return HealthCheckResult.Healthy("Redis is operational", new Dictionary<string, object>
            {
                ["latency_ms"] = Math.Round(latency.TotalMilliseconds, 2)
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Redis health check failed");
            return HealthCheckResult.Unhealthy("Redis health check failed", ex);
        }
    }

    public void Dispose() => _redis?.Dispose();
}
