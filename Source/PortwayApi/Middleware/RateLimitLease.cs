namespace PortwayApi.Middleware;

/// <summary>Outcome of a rate limit consumption attempt, carries header values</summary>
public readonly record struct RateLimitLease(bool Allowed, int Limit, int Remaining, long ResetUnixSeconds);
