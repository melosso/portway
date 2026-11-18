namespace PortwayApi.Helpers;

/// <summary>
/// PORTWAY_ names for legacy env vars; old names still work, new names win
/// </summary>
public static class EnvAliases
{
    // (new PORTWAY_ name, old name, config key the new name should land under, or null for a direct-read var); a key ending in [] takes a comma-separated list
    private static readonly (string NewName, string OldName, string? ConfigKey)[] Map =
    [
        ("PORTWAY_ADMIN_KEY", "Console__AdminApiKey", "Console:AdminApiKey"),
        ("PORTWAY_CONSOLE_ENABLED", "Console__Enabled", "Console:Enabled"),
        ("PORTWAY_ALLOWED_HOSTS", "AllowedHosts", "AllowedHosts"),
        ("PORTWAY_PATH_BASE", "PathBase", "PathBase"),
        ("PORTWAY_SECURE_COOKIES", "Console__SecureCookies", "Console:SecureCookies"),
        ("PORTWAY_OPENAPI_MARKDOWN_ENABLED", "OpenApi__ServeMarkdown", "OpenApi:ServeMarkdown"),
        ("PORTWAY_OPENAPI_DESCRIPTION", "OpenApi__Description", "OpenApi:Description"),
        ("PORTWAY_PUBLIC_ORIGINS", "Console__PublicOrigins__0", "Console:PublicOrigins[]"),
        ("PORTWAY_SEED_PASSWORD", "Console__SeedPassword", "Console:SeedPassword"),
        ("PORTWAY_SEED_TOKEN", "Console__SeedToken", "Console:SeedToken"),
        ("PORTWAY_PROMO_TEXT", "Console__Customization__PromoText", "Console:Customization:PromoText"),
        ("PORTWAY_LOGIN_FOOTER", "Console__Customization__LoginFooter", "Console:Customization:LoginFooter"),
        ("PORTWAY_PROMO_LOGIN_ENABLED", "Console__Customization__PromoLogin", "Console:Customization:PromoLogin"),
        ("PORTWAY_KNOWN_PROXIES", "ForwardedHeaders__KnownProxies__0", "ForwardedHeaders:KnownProxies[]"),
        ("PORTWAY_KNOWN_NETWORKS", "ForwardedHeaders__KnownNetworks__0", "ForwardedHeaders:KnownNetworks[]"),
        ("PORTWAY_OIDC_ENABLED", "Oidc__Enabled", "Oidc:Enabled"),
        ("PORTWAY_MCP_ENABLED", "Mcp__Enabled", "Mcp:Enabled"),
        ("PORTWAY_MCP_CHAT_ENABLED", "Mcp__ChatEnabled", "Mcp:ChatEnabled"),
        ("PORTWAY_TELEMETRY_PROVIDER", "Telemetry__Provider", "Telemetry:Provider"),
        ("PORTWAY_TELEMETRY_OTLP_ENDPOINT", "Telemetry__Otlp__Endpoint", "Telemetry:Otlp:Endpoint"),
        ("PORTWAY_TELEMETRY_SERVICE_NAME", "Telemetry__ServiceName", "Telemetry:ServiceName"),
        ("PORTWAY_PORT_API", "Hosting__ApiPort", "Hosting:ApiPort"),
        ("PORTWAY_PORT_CONSOLE", "Hosting__ConsolePort", "Hosting:ConsolePort"),
        ("PORTWAY_HTTPS_ENABLED", "Use_HTTPS", null),
        ("PORTWAY_PROXY_USERNAME", "PROXY_USERNAME", null),
        ("PORTWAY_PROXY_PASSWORD", "PROXY_PASSWORD", null),
        ("PORTWAY_PROXY_DOMAIN", "PROXY_DOMAIN", null),
        ("PORTWAY_KEY_VAULT_URI", "KEYVAULT_URI", null),
        ("PORTWAY_MCP_CHAT_API_KEY", "PORTWAY_CHAT_API_KEY", null),
    ];

    private static readonly (string FormerName, string CurrentName)[] Renamed =
    [
        ("PORTWAY_WEBUI_ENABLED", "PORTWAY_CONSOLE_ENABLED"),
        ("PORTWAY_OPENAPI_MARKDOWN", "PORTWAY_OPENAPI_MARKDOWN_ENABLED"),
        ("PORTWAY_PROMO_LOGIN", "PORTWAY_PROMO_LOGIN_ENABLED"),
        ("PORTWAY_CHAT_ENABLED", "PORTWAY_MCP_CHAT_ENABLED"),
        ("PORTWAY_OTLP_ENDPOINT", "PORTWAY_TELEMETRY_OTLP_ENDPOINT"),
        ("PORTWAY_SERVICE_NAME", "PORTWAY_TELEMETRY_SERVICE_NAME"),
        ("PORTWAY_USE_HTTPS", "PORTWAY_HTTPS_ENABLED"),
        ("PORTWAY_KEYVAULT_URI", "PORTWAY_KEY_VAULT_URI"),
    ];

    private static string? FormerNameOf(string currentName) => Array.Find(Renamed, r => r.CurrentName == currentName).FormerName;

    private static string? ReadCurrentOrFormer(string newName, Func<string, string?> readEnv) =>
        readEnv(newName) ?? (FormerNameOf(newName) is { } former ? readEnv(former) : null);

    /// Injects PORTWAY_-prefixed values under their canonical config key. Call before builder.Build().
    public static void ApplyConfigAliases(IConfigurationBuilder config)
    {
        var overrides = Resolve(Environment.GetEnvironmentVariable);
        if (overrides.Count > 0)
            config.AddInMemoryCollection(overrides);
    }

    internal static Dictionary<string, string?> Resolve(Func<string, string?> readEnv)
    {
        var overrides = new Dictionary<string, string?>();
        foreach (var (newName, _, configKey) in Map)
        {
            if (configKey is null || ReadCurrentOrFormer(newName, readEnv) is not { } value) continue;
            if (!configKey.EndsWith("[]", StringComparison.Ordinal))
            {
                overrides[configKey] = value;
                continue;
            }

            // legacy indexed entries past the list length still merge in so set one form only
            var items = value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (var i = 0; i < items.Length; i++)
                overrides[$"{configKey[..^2]}:{i}"] = items[i];
        }
        return overrides;
    }

    /// <summary>
    /// Returns the name of the environment variable that sets the config key, or null
    /// </summary>
    public static string? EnvNameFor(string configKey, IEnumerable<string> envNames)
    {
        var alias = Array.Find(Map, m => m.ConfigKey is { } k && k.TrimEnd('[', ']').Equals(configKey, StringComparison.OrdinalIgnoreCase)).NewName;
        var former = alias is null ? null : FormerNameOf(alias);
        string? native = null;
        foreach (var name in envNames)
        {
            if (name == alias || name == former) return name;
            if (native is null && IsNativeNameFor(name, configKey)) native = name;
        }
        return native;
    }

    // list entries end in a numeric index
    private static bool IsNativeNameFor(string name, string configKey)
    {
        var key = name.Replace("__", ":");
        if (key.Equals(configKey, StringComparison.OrdinalIgnoreCase)) return true;
        return key.Length > configKey.Length + 1
            && key.StartsWith(configKey + ":", StringComparison.OrdinalIgnoreCase)
            && key.AsSpan(configKey.Length + 1).ContainsAnyExceptInRange('0', '9') is false;
    }

    /// Reads a direct (non-config-bound) env var, preferring its PORTWAY_ alias over the legacy name.
    public static string? GetDirect(string newName) => GetDirect(newName, Environment.GetEnvironmentVariable);

    internal static string? GetDirect(string newName, Func<string, string?> readEnv)
    {
        var value = ReadCurrentOrFormer(newName, readEnv);
        if (value is not null)
            return value;

        var oldName = Array.Find(Map, m => m.NewName == newName).OldName;
        return oldName is not null ? readEnv(oldName) : null;
    }

    internal static IReadOnlyList<(string NewName, string OldName, string? ConfigKey)> Entries => Map;

    internal static IReadOnlyList<(string FormerName, string CurrentName)> RenamedEntries => Renamed;

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

        foreach (var (formerName, currentName) in Renamed)
        {
            if (Environment.GetEnvironmentVariable(currentName) is null &&
                Environment.GetEnvironmentVariable(formerName) is not null)
            {
                Serilog.Log.Warning(
                    "Environment variable {OldName} is deprecated, use {NewName} instead",
                    formerName, currentName);
            }
        }
    }
}
