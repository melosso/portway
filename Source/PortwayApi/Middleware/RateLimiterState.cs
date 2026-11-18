namespace PortwayApi.Middleware;

using System.Collections.Concurrent;

/// <summary>
/// Shared penalty box state, singleton so the diagnostics endpoint can observe the middleware
/// </summary>
public sealed class RateLimiterState
{
    /// <summary>
    /// IPs in the penalty box with block expiry and last log time
    /// </summary>
    public ConcurrentDictionary<string, (DateTimeOffset BlockedUntil, DateTimeOffset LastLogged)> BlockedIps { get; } = new();

    /// <summary>
    /// Tokens in the penalty box keyed by bucket key
    /// </summary>
    public ConcurrentDictionary<string, TokenBlockInfo> BlockedTokens { get; } = new();
}

/// <summary>
/// Block window for a rate limited token, duration grows with consecutive violations
/// </summary>
public sealed record TokenBlockInfo(DateTimeOffset BlockedUntil, int ConsecutiveBlocks, int BlockDuration)
{
    /// <summary>
    /// True once the token stays quiet for one block duration after the block ends; the next violation counts as the first
    /// </summary>
    public bool IsForgotten(DateTimeOffset now) => now >= BlockedUntil.AddSeconds(BlockDuration);
}
