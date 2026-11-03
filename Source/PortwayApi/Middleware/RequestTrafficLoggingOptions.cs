namespace PortwayApi.Middleware;
/// <summary>
/// Options bound from the RequestTrafficLogging section
/// </summary>
public class RequestTrafficLoggingOptions
{
    public bool Enabled { get; set; } = false;
    public int QueueCapacity { get; set; } = 10000;
    public string Provider { get; set; } = "File";
    public string SqlitePath { get; set; } = "log/traffic_logs.db";
    public string LogDirectory { get; set; } = "log/traffic";
    public int MaxFileSizeMB { get; set; } = 50;
    public int MaxFileCount { get; set; } = 10;
    public string FilePrefix { get; set; } = "proxy_traffic_";
    public int BatchSize { get; set; } = 100;
    public int FlushIntervalMs { get; set; } = 1000;
    public bool CaptureRequestBodies { get; set; } = false;
    public bool CaptureResponseBodies { get; set; } = false;
    public int MaxBodyCaptureSizeBytes { get; set; } = 4096;
    public bool CaptureHeaders { get; set; } = true;
    public bool InfoLoggingEnabled { get; set; } = true;
}
