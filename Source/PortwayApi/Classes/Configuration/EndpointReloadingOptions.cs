namespace PortwayApi.Classes.Configuration;

/// <summary>
/// Configuration options for endpoint hot-reload functionality
/// </summary>
public class EndpointReloadingOptions
{
    /// <summary>
    /// Master kill switch - enables/disables endpoint hot-reload
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Debounce time in milliseconds to prevent duplicate reload events
    /// </summary>
    public int DebounceMs { get; set; } = 2000;

    /// <summary>
    /// Log level for endpoint reload events (Information, Debug, Warning)
    /// </summary>
    public string LogLevel { get; set; } = "Information";
}
