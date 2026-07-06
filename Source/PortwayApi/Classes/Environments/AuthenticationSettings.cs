using System.Collections.Generic;

namespace PortwayApi.Classes;

/// <summary>Settings for custom environment-level authentication</summary>
public class AuthenticationSettings
{
    /// <summary>Whether custom authentication is enabled for this environment</summary>
    public bool Enabled { get; set; } = false;
    
    /// <summary>If true, ONLY custom authentication is allowed. Global tokens are ignored for this environment. If false (default), global tokens can still be used if custom auth fails</summary>
    public bool OverrideGlobalToken { get; set; } = false;
    
    /// <summary>List of allowed authentication methods (OR logic)</summary>
    public List<AuthenticationMethod> Methods { get; set; } = new();
}
