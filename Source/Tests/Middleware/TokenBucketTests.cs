using PortwayApi.Middleware;
using PortwayApi.Tests.Support;
using Xunit;

namespace PortwayApi.Tests.Middleware;

public class TokenBucketTests
{
    [Fact]
    public void TryConsume_RefillsContinuouslyOverTime()
    {
        var clock = new FakeTimeProvider();
        var bucket = new TokenBucket(capacity: 10, refillTime: TimeSpan.FromSeconds(10), clock);

        // Drain the bucket completely
        for (var i = 0; i < 10; i++)
            Assert.True(bucket.TryConsume(1).Allowed);
        Assert.False(bucket.TryConsume(1).Allowed);

        // 1 token per second refill rate; after 3 seconds exactly 3 fit
        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.True(bucket.TryConsume(1).Allowed);
        Assert.True(bucket.TryConsume(1).Allowed);
        Assert.True(bucket.TryConsume(1).Allowed);
        Assert.False(bucket.TryConsume(1).Allowed);
    }

    [Fact]
    public void TryConsume_NeverExceedsCapacity()
    {
        var clock = new FakeTimeProvider();
        var bucket = new TokenBucket(capacity: 5, refillTime: TimeSpan.FromSeconds(5), clock);

        clock.Advance(TimeSpan.FromHours(1));
        var lease = bucket.TryConsume(1);

        Assert.True(lease.Allowed);
        Assert.Equal(4, lease.Remaining);
        Assert.Equal(5, lease.Limit);
    }

    [Fact]
    public void Lease_ResetReflectsFullReplenishment()
    {
        var clock = new FakeTimeProvider();
        var bucket = new TokenBucket(capacity: 10, refillTime: TimeSpan.FromSeconds(10), clock);

        var lease = bucket.TryConsume(1);

        // 1 token consumed refills in 1 second at 1 token/s
        Assert.Equal(clock.GetUtcNow().AddSeconds(1).ToUnixTimeSeconds(), lease.ResetUnixSeconds);
    }

    [Fact]
    public async Task InMemoryStore_EvictsIdleBuckets()
    {
        var clock = new FakeTimeProvider();
        using var store = new InMemoryRateLimiterStore(clock);

        await store.TryConsumeAsync("ip:10.0.0.1", 10, 60);
        await store.TryConsumeAsync("token:abc", 10, 60);
        Assert.Equal(2, store.BucketCount);

        // One bucket stays active, the other goes idle past the eviction age
        clock.Advance(TimeSpan.FromMinutes(29));
        await store.TryConsumeAsync("ip:10.0.0.1", 10, 60);
        clock.Advance(TimeSpan.FromMinutes(2));

        store.EvictIdleBuckets();

        Assert.Equal(1, store.BucketCount);
    }
}
