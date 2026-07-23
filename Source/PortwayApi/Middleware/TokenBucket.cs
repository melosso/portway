namespace PortwayApi.Middleware;

/// <summary>Continuous-refill token bucket, thread-safe via a short lock</summary>
public class TokenBucket
{
    private readonly int _capacity;
    private readonly TimeSpan _refillTime;
    private readonly TimeProvider _timeProvider;
    private readonly object _syncLock = new object();

    private double _tokens;
    private DateTimeOffset _lastRefill;
    private DateTimeOffset _lastActivity;

    public TokenBucket(int capacity, TimeSpan refillTime, TimeProvider timeProvider)
    {
        _capacity = capacity;
        _refillTime = refillTime;
        _timeProvider = timeProvider;
        _tokens = capacity;
        _lastRefill = timeProvider.GetUtcNow();
        _lastActivity = _lastRefill;
    }

    public int Capacity => _capacity;

    /// <summary>Time of the last consumption attempt, used for idle eviction</summary>
    public DateTimeOffset LastActivity
    {
        get { lock (_syncLock) return _lastActivity; }
    }

    /// <summary>Attempts to take tokens and reports the post-attempt bucket state</summary>
    public RateLimitLease TryConsume(int tokenCount)
    {
        lock (_syncLock)
        {
            var now = _timeProvider.GetUtcNow();
            _lastActivity = now;
            RefillTokens(now);

            bool allowed = _tokens >= tokenCount;
            if (allowed)
                _tokens -= tokenCount;

            return BuildLease(allowed, now);
        }
    }

    /// <summary>Reports current bucket state without consuming</summary>
    public RateLimitLease Peek()
    {
        lock (_syncLock)
        {
            var now = _timeProvider.GetUtcNow();
            RefillTokens(now);
            return BuildLease(true, now);
        }
    }

    private RateLimitLease BuildLease(bool allowed, DateTimeOffset now)
    {
        // Reset marks the moment the bucket is fully replenished
        var secondsToFull = (_capacity - _tokens) * _refillTime.TotalSeconds / _capacity;
        return new RateLimitLease(
            allowed,
            _capacity,
            (int)Math.Floor(_tokens),
            now.AddSeconds(secondsToFull).ToUnixTimeSeconds());
    }

    private void RefillTokens(DateTimeOffset now)
    {
        var elapsed = (now - _lastRefill).TotalSeconds;
        if (elapsed <= 0)
            return;

        var tokensToAdd = elapsed * (_capacity / _refillTime.TotalSeconds);
        _tokens = Math.Min(_capacity, _tokens + tokensToAdd);
        _lastRefill = now;
    }
}
