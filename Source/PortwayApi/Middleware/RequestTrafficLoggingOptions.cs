namespace PortwayApi.Middleware;
/// <summary>
/// RequestTrafficLogging section options.
/// </summary>
public sealed record RequestTrafficLoggingOptions
{
    public bool Enabled { get; init; } = false;
    public int QueueCapacity { get; init; } = 10000;
    public string Provider { get; init; } = "File";
    public string SqlitePath { get; init; } = "log/traffic_logs.db";
    public string LogDirectory { get; init; } = "log/traffic";
    public int MaxFileSizeMB { get; init; } = 50;
    public int MaxFileCount { get; init; } = 10;
    public string FilePrefix { get; init; } = "proxy_traffic_";
    public int BatchSize { get; init; } = 100;
    public int FlushIntervalMs { get; init; } = 1000;
    public bool CaptureRequestBodies { get; init; } = false;
    public bool CaptureResponseBodies { get; init; } = false;
    public int MaxBodyCaptureSizeBytes { get; init; } = 4096;
    public bool CaptureHeaders { get; init; } = true;
    public bool InfoLoggingEnabled { get; init; } = true;
}
