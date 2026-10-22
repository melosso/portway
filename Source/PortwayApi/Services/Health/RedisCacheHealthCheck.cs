using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using PortwayApi.Services.Caching;
using Serilog;

namespace PortwayApi.Services.Health;

/// <summary>
/// Health check for the Redis cache connection used by <see cref="RedisCacheProvider"/>
/// </summary>
public sealed class RedisCacheHealthCheck(RedisCacheProvider cache, IOptions<CacheOptions> options) : IHealthCheck
{
    // per instance key so concurrent probes from other instances cannot overwrite it
    private readonly string _probeKey = $"health:check:{Guid.NewGuid():N}";

    // the memory fallback keeps serving while redis is down
    private readonly HealthStatus _failureStatus = options.Value.Redis.FallbackToMemoryCache
        ? HealthStatus.Degraded
        : HealthStatus.Unhealthy;

    /// <summary>
    /// Performs the health check
    /// </summary>
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!cache.IsConnected)
        {
            return new HealthCheckResult(_failureStatus, "Redis connection is not established");
        }

        try
        {
            var latency = await cache.ProbeAsync(_probeKey).WaitAsync(cancellationToken);

            return latency is { } roundTrip
                ? HealthCheckResult.Healthy("Redis is operational", new Dictionary<string, object>
                {
                    ["latency_ms"] = Math.Round(roundTrip.TotalMilliseconds, 2)
                })
                : new HealthCheckResult(_failureStatus, "Redis set/get operations not working correctly");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error(ex, "Redis health check failed");
            return new HealthCheckResult(_failureStatus, "Redis health check failed", ex);
        }
    }
}
