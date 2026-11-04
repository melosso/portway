using PortwayApi.Middleware;
using PortwayApi.Tests.Parity;
using PortwayApi.Tests.Support;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Testcontainers.Redis;
using Xunit;

namespace PortwayApi.Tests.Middleware;

[Collection("Integration")]
public sealed class RedisRateLimiterStoreTests
{
    [DockerFact]
    public async Task SharedBucket_IgnoresClockSkewBetweenInstances()
    {
        await using var container = new RedisBuilder("redis:8-alpine").Build();
        await container.StartAsync(TestContext.Current.CancellationToken);
        var clock = new FakeTimeProvider();
        var skewed = new FakeTimeProvider();
        skewed.Advance(TimeSpan.FromSeconds(60));
        using var first = new RedisRateLimiterStore(container.GetConnectionString(), new InMemoryRateLimiterStore(clock), clock);
        using var second = new RedisRateLimiterStore(container.GetConnectionString(), new InMemoryRateLimiterStore(skewed), skewed);

        await first.TryConsumeAsync("skew", 2, 60, TestContext.Current.CancellationToken);
        await first.TryConsumeAsync("skew", 2, 60, TestContext.Current.CancellationToken);
        var lease = await second.TryConsumeAsync("skew", 2, 60, TestContext.Current.CancellationToken);

        Assert.False(lease.Allowed);
    }

    private sealed class CollectingSink : ILogEventSink
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<LogEvent> _events = new();

        public IReadOnlyCollection<LogEvent> Events => _events;

        public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);
    }

    [DockerFact]
    public async Task RedisOutage_LogsOneWarningAndOneRecovery()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var container = new RedisBuilder("redis:8-alpine").Build();
        await container.StartAsync(ct);
        var clock = new FakeTimeProvider();
        using var store = new RedisRateLimiterStore(container.GetConnectionString() + ",asyncTimeout=200,syncTimeout=200", new InMemoryRateLimiterStore(clock), clock);
        await store.TryConsumeAsync("outage", 100, 60, ct);

        var sink = new CollectingSink();
        var previous = Log.Logger;
        Log.Logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
        try
        {
            await container.PauseAsync(ct);
            for (var i = 0; i < 3; i++)
                await store.TryConsumeAsync("outage", 100, 60, ct);
            await container.UnpauseAsync(ct);

            // Reconnect can take several attempts
            var deadline = DateTime.UtcNow.AddSeconds(15);
            do
            {
                await store.TryConsumeAsync("outage", 100, 60, ct);
                if (sink.Events.Any(e => e.Level == LogEventLevel.Information)) break;
                await Task.Delay(100, ct);
            } while (DateTime.UtcNow < deadline);
        }
        finally
        {
            Log.Logger = previous;
        }

        var rateLimitEvents = sink.Events.Where(e => e.RenderMessage().Contains("rate limit", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Single(rateLimitEvents, e => e.Level == LogEventLevel.Warning);
        Assert.Single(rateLimitEvents, e => e.Level == LogEventLevel.Information);
    }
}
