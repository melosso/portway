namespace PortwayApi.Middleware;

public class RateLimitSettings
{
    public bool Enabled { get; set; } = true;
    public int IpLimit { get; set; } = 100;
    public int IpWindow { get; set; } = 60; // seconds
    public int TokenLimit { get; set; } = 1000;
    public int TokenWindow { get; set; } = 60; // seconds

    /// <summary>Bucket store: Memory (default) or Redis for multi-instance deployments</summary>
    public string Store { get; set; } = "Memory";

    /// <summary>Redis connection for the rate limit store, falls back to Caching:Redis:ConnectionString when empty</summary>
    public string? RedisConnectionString { get; set; }
}
