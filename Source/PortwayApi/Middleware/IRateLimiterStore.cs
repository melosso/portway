namespace PortwayApi.Middleware;

/// <summary>Backing store for token bucket rate limiting, in-memory by default with Redis as opt-in</summary>
public interface IRateLimiterStore
{
    /// <summary>Atomically consumes one token from the bucket for the given key</summary>
    ValueTask<RateLimitLease> TryConsumeAsync(string key, int limit, int windowSeconds, CancellationToken cancellationToken = default);
}
