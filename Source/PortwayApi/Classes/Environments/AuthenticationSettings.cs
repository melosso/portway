namespace PortwayApi.Classes;

/// <summary>
/// Settings for custom environment-level authentication
/// </summary>
public class AuthenticationSettings
{
    public static readonly (HashSet<string> Headers, HashSet<string> Query, HashSet<string> Cookies) NoCredentialNames = ([], [], []);

    /// <summary>
    /// Client values under these names authenticate to Portway and are not sent upstream
    /// </summary>
    public (HashSet<string> Headers, HashSet<string> Query, HashSet<string> Cookies) CredentialNames()
    {
        var names = (Headers: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            Query: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            Cookies: new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        if (!Enabled)
            return names;

        foreach (var method in Methods ?? [])
        {
            switch (method.Type.ToLowerInvariant())
            {
                case "apikey" when method.In.Equals("query", StringComparison.OrdinalIgnoreCase):
                    names.Query.Add(method.Name);
                    break;
                case "apikey" when method.In.Equals("cookie", StringComparison.OrdinalIgnoreCase):
                    names.Cookies.Add(method.Name);
                    break;
                case "apikey":
                    names.Headers.Add(method.Name);
                    break;
                case "hmac":
                    names.Headers.Add(method.Name.Split('|')[0]);
                    names.Headers.Add("X-Timestamp");
                    break;
            }
        }

        return names;
    }

    /// <summary>
    /// Whether custom authentication is enabled for this environment
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// If true, ONLY custom authentication is allowed. Global tokens are ignored for this environment. If false (default), global tokens can still be used if custom auth fails
    /// </summary>
    public bool OverrideGlobalToken { get; set; } = false;

    /// <summary>
    /// List of allowed authentication methods (OR logic)
    /// </summary>
    public List<AuthenticationMethod> Methods { get; set; } = new();
}
