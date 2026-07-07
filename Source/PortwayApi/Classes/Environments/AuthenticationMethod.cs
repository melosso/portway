using System.Collections.Generic;

namespace PortwayApi.Classes;

/// <summary>Represents a single authentication method (e.g., ApiKey, JWT, Basic)</summary>
public class AuthenticationMethod
{
    /// <summary>Common types: ApiKey, Basic, Bearer, JWT, HMAC</summary>
    public string Type { get; set; } = string.Empty;
    
    /// <summary>Name of the credential identifier (e.g., header name "X-API-Key", or Basic username)</summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>Credential value (e.g., API key, static token, or Basic password). This will be auto-encrypted with current encryption method</summary>
    public string Value { get; set; } = string.Empty;
    
    /// <summary>Where to look for the credential: Header (default), Query, Cookie</summary>
    public string In { get; set; } = "Header";
    
    // JWT/OAuth2 specific properties
    public string? Issuer { get; set; }
    public string? Audience { get; set; }
    public string? Secret { get; set; } // Will be encrypted
    public string? PublicKey { get; set; } // PEM format
    public string? Algorithm { get; set; } // e.g., "HS256", "RS256"
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; } // Will be encrypted
    public string? IntrospectionEndpoint { get; set; }
}
