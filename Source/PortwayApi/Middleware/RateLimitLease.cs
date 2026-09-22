namespace PortwayApi.Middleware;

/// <summary>
/// Outcome of a rate limit consumption attempt, with the values written to the RateLimit-* response headers
/// </summary>
public readonly record struct RateLimitLease(bool Allowed, int Limit, int Remaining, long ResetUnixSeconds);
