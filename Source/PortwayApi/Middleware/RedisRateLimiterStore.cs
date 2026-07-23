namespace PortwayApi.Middleware;

using StackExchange.Redis;
using Serilog;

/// <summary>Opt-in Redis-backed store so limits hold across instances, falls back to memory when Redis is down</summary>
public class RedisRateLimiterStore : IRateLimiterStore, IDisposable
{
    // Atomic token bucket: refill by elapsed time, consume if enough, return state
    private const string _consumeScript = @"
        local capacity = tonumber(ARGV[1])
        local refill_seconds = tonumber(ARGV[2])
        local now_ms = tonumber(ARGV[3])
        local requested = tonumber(ARGV[4])
        local state = redis.call('HMGET', KEYS[1], 'tokens', 'last_ms')
        local tokens = tonumber(state[1]) or capacity
        local last_ms = tonumber(state[2]) or now_ms
        local elapsed = math.max(0, now_ms - last_ms) / 1000.0
        tokens = math.min(capacity, tokens + elapsed * (capacity / refill_seconds))
        local allowed = 0
        if tokens >= requested then
            tokens = tokens - requested
            allowed = 1
        end
        redis.call('HSET', KEYS[1], 'tokens', tokens, 'last_ms', now_ms)
        redis.call('EXPIRE', KEYS[1], math.ceil(refill_seconds * 2))
        local reset = math.ceil((capacity - tokens) * refill_seconds / capacity)
        return {allowed, math.floor(tokens), reset}";

    private readonly InMemoryRateLimiterStore _fallback;
    private readonly TimeProvider _timeProvider;
    private readonly ConnectionMultiplexer? _redis;
    private readonly IDatabase? _db;

    public RedisRateLimiterStore(string connectionString, InMemoryRateLimiterStore fallback, TimeProvider timeProvider)
    {
        _fallback = fallback;
        _timeProvider = timeProvider;

        try
        {
            var options = ConfigurationOptions.Parse(connectionString);
            options.AbortOnConnectFail = false;
            _redis = ConnectionMultiplexer.Connect(options);
            _db = _redis.GetDatabase();
            Log.Information("Rate limiter using Redis store at {ConnectionString}", connectionString);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Rate limiter failed to connect to Redis, falling back to in-memory store");
        }
    }

    public async ValueTask<RateLimitLease> TryConsumeAsync(string key, int limit, int windowSeconds, CancellationToken cancellationToken = default)
    {
        if (_db is null || _redis is not { IsConnected: true })
            return await _fallback.TryConsumeAsync(key, limit, windowSeconds, cancellationToken);

        try
        {
            var now = _timeProvider.GetUtcNow();
            var result = (RedisResult[]?)await _db.ScriptEvaluateAsync(
                _consumeScript,
                new RedisKey[] { $"portway:ratelimit:{key}" },
                new RedisValue[] { limit, windowSeconds, now.ToUnixTimeMilliseconds(), 1 });

            if (result is not { Length: 3 })
                return await _fallback.TryConsumeAsync(key, limit, windowSeconds, cancellationToken);

            return new RateLimitLease(
                (long)result[0] == 1,
                limit,
                (int)(long)result[1],
                now.ToUnixTimeSeconds() + (long)result[2]);
        }
        catch (Exception ex)
        {
            // Fail open to the local store; rate limiting must never take the gateway down
            Log.Warning(ex, "Redis rate limit evaluation failed, using in-memory fallback");
            return await _fallback.TryConsumeAsync(key, limit, windowSeconds, cancellationToken);
        }
    }

    public void Dispose() => _redis?.Dispose();
}
