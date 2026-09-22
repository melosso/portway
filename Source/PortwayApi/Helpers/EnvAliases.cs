namespace PortwayApi.Helpers;

/// PORTWAY_-prefixed names for env vars that used to be unprefixed or ASP.NET's
/// double-underscore config binding. Old names still work; new names win when both are set.
public static class EnvAliases
{
    // (new PORTWAY_ name, old name, config key the new name should land under, or null for a direct-read var)
    private static readonly (string NewName, string OldName, string? ConfigKey)[] Map =
    [
        ("PORTWAY_ADMIN_KEY", "WebUi__AdminApiKey", "WebUi:AdminApiKey"),
        ("PORTWAY_ALLOWED_HOSTS", "AllowedHosts", "AllowedHosts"),
        ("PORTWAY_PATH_BASE", "PathBase", "PathBase"),
        ("PORTWAY_SECURE_COOKIES", "WebUi__SecureCookies", "WebUi:SecureCookies"),
        ("PORTWAY_USE_HTTPS", "Use_HTTPS", null),
        ("PORTWAY_PROXY_USERNAME", "PROXY_USERNAME", null),
        ("PORTWAY_PROXY_PASSWORD", "PROXY_PASSWORD", null),
        ("PORTWAY_PROXY_DOMAIN", "PROXY_DOMAIN", null),
        ("PORTWAY_KEYVAULT_URI", "KEYVAULT_URI", null),
    ];

    /// Injects PORTWAY_-prefixed values under their canonical config key. Call before builder.Build().
    public static void ApplyConfigAliases(IConfigurationBuilder config)
    {
        var overrides = new Dictionary<string, string?>();
        foreach (var (newName, _, configKey) in Map)
        {
            if (configKey is null) continue;
            var value = Environment.GetEnvironmentVariable(newName);
            if (value is not null) overrides[configKey] = value;
        }
        if (overrides.Count > 0)
            config.AddInMemoryCollection(overrides);
    }

    /// Reads a direct (non-config-bound) env var, preferring its PORTWAY_ alias over the legacy name.
    public static string? GetDirect(string newName)
    {
        var value = Environment.GetEnvironmentVariable(newName);
        if (value is not null)
            return value;

        var oldName = Array.Find(Map, m => m.NewName == newName).OldName;
        return oldName is not null ? Environment.GetEnvironmentVariable(oldName) : null;
    }

    /// Logs one warning per legacy var still in use without its PORTWAY_ replacement. Call once Serilog is up.
    public static void WarnOnLegacyUsage()
    {
        foreach (var (newName, oldName, _) in Map)
        {
            if (Environment.GetEnvironmentVariable(newName) is null &&
                Environment.GetEnvironmentVariable(oldName) is not null)
            {
                Serilog.Log.Warning(
                    "Environment variable {OldName} is deprecated, use {NewName} instead",
                    oldName, newName);
            }
        }
    }
}
