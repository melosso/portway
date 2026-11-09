namespace PortwayApi.Middleware;

public sealed class RateLimitSettings
{
    public bool Enabled { get; set; } = true;
    public int IpLimit { get; set; } = 100;
    public int IpWindowSeconds { get; set; } = 60;
    public int TokenLimit { get; set; } = 1000;
    public int TokenWindowSeconds { get; set; } = 60;

    /// <summary>
    /// Bucket store: Memory (default) or Redis for multi-instance deployments
    /// </summary>
    public string Provider { get; set; } = "Memory";
}
