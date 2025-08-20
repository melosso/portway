using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Serilog;

namespace PortwayApi.Services.Health;

/// <summary>
/// Health check for Redis cache
/// </summary>
public class RedisCacheHealthCheck : IHealthCheck
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
                Log.Warning(ex, "❌ Failed to initialize Redis connection for health check");
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

            // Get Redis info for reporting
            var endPoints = _redis.GetEndPoints();
            var serverInfo = new Dictionary<string, object>();
            
            foreach (var endpoint in endPoints)
            {
                var server = _redis.GetServer(endpoint);
                if (server.IsConnected)
                {
                    // Gather info about the server
                    var info = await server.InfoAsync("server");
                    var memory = await server.InfoAsync("memory");
                    var clients = await server.InfoAsync("clients");
                    
                    // Extract key metrics
                    var serverData = new Dictionary<string, string>();
                    
                    // Flatten groupings and add key-value pairs
                    foreach (var grouping in info)
                    {
                        foreach (var pair in grouping)
                        {
                            serverData[pair.Key] = pair.Value;
                        }
                    }
                    foreach (var grouping in memory)
                    {
                        foreach (var pair in grouping)
                        {
                            serverData[pair.Key] = pair.Value;
                        }
                    }
                    foreach (var grouping in clients)
                    {
                        foreach (var pair in grouping)
                        {
                            serverData[pair.Key] = pair.Value;
                        }
                    }
                    
                    serverInfo[endpoint?.ToString() ?? "unknown"] = serverData;
                }
                else
                {
                    serverInfo[endpoint?.ToString() ?? "unknown"] = "Not connected";
                }
            }

            // Return healthy with server info
            return HealthCheckResult.Healthy("Redis is operational", serverInfo);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ Redis health check failed");
            return HealthCheckResult.Unhealthy("Redis health check failed", ex);
        }
    }
}
