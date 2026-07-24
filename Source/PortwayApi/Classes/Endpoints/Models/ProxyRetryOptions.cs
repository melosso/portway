namespace PortwayApi.Classes;

/// <summary>Retry settings for proxy endpoint upstream calls</summary>
public sealed class ProxyRetryOptions
{
    public int Attempts { get; set; } = 1;
    public int DelayMs { get; set; } = 200;
}
