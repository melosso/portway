namespace PortwayApi.Classes;

/// <summary>
/// Root configuration model for an environment's settings.json
/// </summary>
public sealed class EnvironmentConfig
{
    /// <summary>
    /// Connection string for the environment's database (SQL Server)
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Server name or data source
    /// </summary>
    public string? ServerName { get; set; }

    /// <summary>
    /// Default headers to include in proxy requests for this environment
    /// </summary>
    public Dictionary<string, string> Headers { get; set; } = new();

    /// <summary>
    /// Custom authentication settings for this environment
    /// </summary>
    public AuthenticationSettings? Authentication { get; set; }

    /// <summary>
    /// Set false to keep this settings.json plaintext, skips at-rest encryption on load. Meant for dev checkouts. Null means encrypt (default)
    /// </summary>
    public bool? Encrypt { get; set; }

    /// <summary>
    /// Named secret values referenced as <c>${secret:NAME}</c>, encrypted at rest
    /// </summary>
    public Dictionary<string, string>? Secrets { get; set; }

    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? ExtensionData { get; set; }
}
