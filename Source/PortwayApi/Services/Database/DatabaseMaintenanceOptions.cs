namespace PortwayApi.Services.Database;

/// <summary>Settings for the nightly SQLite maintenance run (ANALYZE always, VACUUM when bloated)</summary>
public class DatabaseMaintenanceOptions
{
    public bool Enabled { get; set; } = true;
    /// <summary>Local time of day the run starts, HH:mm</summary>
    public string Schedule { get; set; } = "03:00";
    /// <summary>VACUUM only when freelist pages exceed this fraction of total pages</summary>
    public double FreePageRatioThreshold { get; set; } = 0.25;
    /// <summary>Run maintenance once shortly after startup in addition to the schedule</summary>
    public bool RunOnStartup { get; set; } = false;
}
