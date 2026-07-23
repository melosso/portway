using System.Collections.Generic;

namespace PortwayApi.Classes;

/// <summary>Root configuration model for an environment's settings.json</summary>
public class EnvironmentConfig
{
    /// <summary>Connection string for the environment's database (SQL Server)</summary>
    public string? ConnectionString { get; set; }
    
    /// <summary>Server name or data source</summary>
    public string? ServerName { get; set; }
    
    /// <summary>Default headers to include in proxy requests for this environment</summary>
    public Dictionary<string, string> Headers { get; set; } = new();
    
    /// <summary>Custom authentication settings for this environment</summary>
    public AuthenticationSettings? Authentication { get; set; }

    /// <summary>Set false to keep this settings.json plaintext, skips at-rest encryption on load. Meant for dev checkouts. Null means encrypt (default)</summary>
    public bool? Encrypt { get; set; }
}
