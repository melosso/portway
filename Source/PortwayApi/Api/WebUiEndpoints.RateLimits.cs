namespace PortwayApi.Endpoints;

using Microsoft.AspNetCore.Builder;
using PortwayApi.Middleware;

public static partial class WebUiEndpointExtensions
{
    private static void MapRateLimitRoutes(WebApplication app)
    {
        // Live rate limiter state: penalty boxes plus in-memory bucket snapshot
        app.MapGet("/ui/api/ratelimits", (
            RateLimiterState state,
            IRateLimiterStore store,
            RateLimitSettings settings,
            TimeProvider timeProvider) =>
        {
            var now = timeProvider.GetUtcNow();

            var blockedIps = state.BlockedIps
                .Where(kv => kv.Value.BlockedUntil > now)
                .Select(kv => new
                {
                    ip = kv.Key,
                    blocked_until = kv.Value.BlockedUntil.ToString("o"),
                    remaining_seconds = (int)(kv.Value.BlockedUntil - now).TotalSeconds
                })
                .OrderByDescending(b => b.remaining_seconds)
                .ToList();

            var blockedTokens = state.BlockedTokens
                .Where(kv => kv.Value.BlockedUntil > now)
                .Select(kv => new
                {
                    token = MaskBucketKey(kv.Key),
                    blocked_until = kv.Value.BlockedUntil.ToString("o"),
                    remaining_seconds = (int)(kv.Value.BlockedUntil - now).TotalSeconds,
                    consecutive_blocks = kv.Value.ConsecutiveBlocks
                })
                .OrderByDescending(b => b.remaining_seconds)
                .ToList();

            // Bucket detail is only observable on the in-memory store, Redis reports counts as unavailable
            var memoryStore = store as InMemoryRateLimiterStore;
            var buckets = memoryStore?.Snapshot()
                .Select(s => new
                {
                    key = MaskBucketKey(s.Key),
                    resource = s.Key.StartsWith("ip:", StringComparison.Ordinal) ? "ip" : "token",
                    limit = s.Lease.Limit,
                    remaining = s.Lease.Remaining,
                    used_percent = s.Lease.Limit > 0 ? (int)Math.Round(100.0 * (s.Lease.Limit - s.Lease.Remaining) / s.Lease.Limit) : 0
                })
                .OrderByDescending(b => b.used_percent)
                .Take(50)
                .ToList();

            return Results.Json(new
            {
                enabled = settings.Enabled,
                store = store.GetType().Name.Replace("RateLimiterStore", ""),
                ip_limit = settings.IpLimit,
                ip_window_seconds = settings.IpWindow,
                token_limit = settings.TokenLimit,
                token_window_seconds = settings.TokenWindow,
                bucket_count = memoryStore?.BucketCount,
                blocked_ips = blockedIps,
                blocked_tokens = blockedTokens,
                buckets
            });
        }).ExcludeFromDescription();
    }

    // Never expose raw token material, keep 4+4 chars of the token portion of a bucket key
    private static string MaskBucketKey(string key)
    {
        if (key.StartsWith("ip:", StringComparison.Ordinal))
            return key;

        var value = key.StartsWith("token:", StringComparison.Ordinal) ? key["token:".Length..] : key;
        // Strip the :limit:window suffix appended by the middleware
        var end = value.IndexOf(':');
        if (end > 0) value = value[..end];

        return value.Length <= 8 ? "token:***" : $"token:{value[..4]}...{value[^4..]}";
    }
}
