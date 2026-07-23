namespace PortwayApi.Middleware;

using System.Collections.Concurrent;
using Serilog;

/// <summary>Default single-instance store, one TokenBucket per key with idle eviction</summary>
public class InMemoryRateLimiterStore : IRateLimiterStore, IDisposable
{
    private readonly ConcurrentDictionary<string, TokenBucket> _buckets = new();
    private readonly TimeProvider _timeProvider;
    private readonly ITimer _evictionTimer;

    // Buckets idle beyond this are dropped; they rebuild full on next request
    private static readonly TimeSpan _idleEvictionAge = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan _evictionInterval = TimeSpan.FromMinutes(5);

    public InMemoryRateLimiterStore(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        _evictionTimer = timeProvider.CreateTimer(_ => EvictIdleBuckets(), null, _evictionInterval, _evictionInterval);
    }

    public ValueTask<RateLimitLease> TryConsumeAsync(string key, int limit, int windowSeconds, CancellationToken cancellationToken = default)
    {
        var bucket = _buckets.GetOrAdd(key, _ => new TokenBucket(limit, TimeSpan.FromSeconds(windowSeconds), _timeProvider));
        return ValueTask.FromResult(bucket.TryConsume(1));
    }

    /// <summary>Number of live buckets, exposed for diagnostics and tests</summary>
    public int BucketCount => _buckets.Count;

    /// <summary>Current state of every bucket without consuming, for the diagnostics endpoint</summary>
    public IEnumerable<(string Key, RateLimitLease Lease)> Snapshot()
    {
        foreach (var pair in _buckets)
            yield return (pair.Key, pair.Value.Peek());
    }

    internal void EvictIdleBuckets()
    {
        var cutoff = _timeProvider.GetUtcNow() - _idleEvictionAge;
        int evicted = 0;

        foreach (var pair in _buckets)
        {
            if (pair.Value.LastActivity < cutoff && _buckets.TryRemove(pair.Key, out _))
                evicted++;
        }

        if (evicted > 0)
            Log.Debug("Rate limiter evicted {Evicted} idle buckets, {Remaining} remain", evicted, _buckets.Count);
    }

    public void Dispose() => _evictionTimer.Dispose();
}
