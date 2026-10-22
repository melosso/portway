using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using PortwayApi.Endpoints;
using PortwayApi.Middleware;
using PortwayApi.Services.Caching;
using PortwayApi.Services.Health;
using PortwayApi.Tests.Parity;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Testcontainers.Redis;
using Xunit;

namespace PortwayApi.Tests.Services;

public class RedisCacheHealthCheckTests
{
    private const string Unreachable = "127.0.0.1:1,connectTimeout=100";

    private static RedisCacheProvider Provider(string connectionString, bool fallbackToMemoryCache = true) => new(
        Options.Create(new CacheOptions
        {
            ProviderType = CacheProviderType.Redis,
            Redis = new RedisOptions { ConnectionString = connectionString, FallbackToMemoryCache = fallbackToMemoryCache }
        }),
        new MemoryCacheProvider(Options.Create(new CacheOptions())));

    private static Task<HealthCheckResult> CheckAsync(RedisCacheProvider provider, bool fallbackToMemoryCache = true) =>
        new RedisCacheHealthCheck(provider, Options.Create(new CacheOptions
        {
            Redis = new RedisOptions { FallbackToMemoryCache = fallbackToMemoryCache }
        })).CheckHealthAsync(new HealthCheckContext());

    [DockerFact]
    public async Task ReportsHealthy_AgainstReachableRedis_WithoutAdminMode()
    {
        await using var container = new RedisBuilder("redis:8-alpine").Build();
        await container.StartAsync();
        using var provider = Provider(container.GetConnectionString());

        var result = await CheckAsync(provider);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.True(result.Data.ContainsKey("latency_ms"));
    }

    [Theory]
    [InlineData(true, HealthStatus.Degraded)]
    [InlineData(false, HealthStatus.Unhealthy)]
    public async Task UnreachableRedis_IsDegradedOnlyWhileMemoryFallbackServes(bool fallbackToMemoryCache, HealthStatus expected)
    {
        using var provider = Provider(Unreachable, fallbackToMemoryCache);

        var result = await CheckAsync(provider, fallbackToMemoryCache);

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public void ResolvesAsSingleton()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Caching:ProviderType"] = "Redis",
                ["Caching:Redis:ConnectionString"] = Unreachable
            })
            .Build();

        using var services = new ServiceCollection().AddCachingServices(configuration).BuildServiceProvider();

        Assert.Same(services.GetRequiredService<RedisCacheHealthCheck>(), services.GetRequiredService<RedisCacheHealthCheck>());
    }

    [Fact]
    public void RedisConnectionLogs_OmitThePassword()
    {
        const string password = "s3cret-redis-pass";
        var connectionString = $"{Unreachable},password={password}";
        var sink = new CollectingSink();
        var previous = Log.Logger;
        Log.Logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Caching:ProviderType"] = "Redis",
                    ["Caching:Redis:ConnectionString"] = connectionString
                })
                .Build();

            new ServiceCollection().AddCachingServices(configuration);
            using (Provider(connectionString)) { }
            using (new RedisRateLimiterStore(connectionString, new InMemoryRateLimiterStore(TimeProvider.System), TimeProvider.System)) { }
        }
        finally
        {
            Log.Logger = previous;
        }

        Assert.NotEmpty(sink.Messages);
        Assert.DoesNotContain(sink.Messages, m => m.Contains(password, StringComparison.Ordinal));
    }

    [Fact]
    public void Health_ServesWhileOnlyTheCacheIsDegraded()
    {
        Assert.True(HealthCheckEndpointExtensions.IsServing(Report(("redis_cache", HealthStatus.Degraded, ["redis", "cache"]))));
        Assert.False(HealthCheckEndpointExtensions.IsServing(Report(("redis_cache", HealthStatus.Unhealthy, ["redis", "cache"]))));
        Assert.False(HealthCheckEndpointExtensions.IsServing(Report(("Diskspace", HealthStatus.Degraded, ["system"]))));
    }

    private static HealthReport Report(params (string Name, HealthStatus Status, string[] Tags)[] entries) => new(
        entries.ToDictionary(e => e.Name, e => new HealthReportEntry(e.Status, null, TimeSpan.Zero, null, null, e.Tags)),
        TimeSpan.Zero);

    private sealed class CollectingSink : ILogEventSink
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _messages = new();

        public IReadOnlyCollection<string> Messages => _messages;

        public void Emit(LogEvent logEvent) => _messages.Enqueue(logEvent.RenderMessage() + logEvent.Exception);
    }
}
