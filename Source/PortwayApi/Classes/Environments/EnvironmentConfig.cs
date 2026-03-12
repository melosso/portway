using System.Collections.Generic;

namespace PortwayApi.Classes;

/// <summary>
/// Root configuration model for an environment's settings.json
/// </summary>
public class EnvironmentConfig
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
}

/// <summary>
/// Settings for custom environment-level authentication
/// </summary>
public class AuthenticationSettings
{
    /// <summary>
    /// Whether custom authentication is enabled for this environment
    /// </summary>
    public bool Enabled { get; set; } = false;
    
    /// <summary>
    /// If true, ONLY custom authentication is allowed. Global tokens are ignored for this environment.
    /// If false (default), global tokens can still be used if custom auth fails.
    /// </summary>
    public bool OverrideGlobalToken { get; set; } = false;
    
    /// <summary>
    /// List of allowed authentication methods (OR logic)
    /// </summary>
    public List<AuthenticationMethod> Methods { get; set; } = new();
}

/// <summary>
/// Represents a single authentication method (e.g., ApiKey, JWT, Basic)
/// </summary>
public class AuthenticationMethod
{
    /// <summary>
    /// Common types: ApiKey, Basic, Bearer, JWT, HMAC
    /// </summary>
    public string Type { get; set; } = string.Empty;
    
    /// <summary>
    /// Name of the credential identifier (e.g., header name "X-API-Key", or Basic username)
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Credential value (e.g., API key, static token, or Basic password). 
    /// This will be auto-encrypted with PWENC: prefix.
    /// </summary>
    public string Value { get; set; } = string.Empty;
    
    /// <summary>
    /// Where to look for the credential: Header (default), Query, Cookie
    /// </summary>
    public string In { get; set; } = "Header";
    
    // JWT/OAuth2 specific properties
    public string? Issuer { get; set; }
    public string? Audience { get; set; }
    public string? Secret { get; set; } // Will be auto-encrypted
    public string? PublicKey { get; set; } // PEM format
    public string? Algorithm { get; set; } // e.g., "HS256", "RS256"
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; } // Will be auto-encrypted
    public string? IntrospectionEndpoint { get; set; }
}
