using PortwayApi.Middleware;
using PortwayApi.Tests.Parity;
using PortwayApi.Tests.Support;
using Testcontainers.Redis;
using Xunit;

namespace PortwayApi.Tests.Middleware;

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
}
