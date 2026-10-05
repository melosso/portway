namespace PortwayApi.Helpers;

/// <summary>
/// PORTWAY_ names for legacy env vars; old names still work, new names win
/// </summary>
public static class EnvAliases
{
    // (new PORTWAY_ name, old name, config key the new name should land under, or null for a direct-read var); a key ending in [] takes a comma-separated list
    private static readonly (string NewName, string OldName, string? ConfigKey)[] Map =
    [
        ("PORTWAY_ADMIN_KEY", "WebUi__AdminApiKey", "WebUi:AdminApiKey"),
        ("PORTWAY_WEBUI_ENABLED", "WebUi__Enabled", "WebUi:Enabled"),
        ("PORTWAY_ALLOWED_HOSTS", "AllowedHosts", "AllowedHosts"),
        ("PORTWAY_PATH_BASE", "PathBase", "PathBase"),
        ("PORTWAY_SECURE_COOKIES", "WebUi__SecureCookies", "WebUi:SecureCookies"),
        ("PORTWAY_OPENAPI_MARKDOWN", "OpenApi__MarkdownEnabled", "OpenApi:MarkdownEnabled"),
        ("PORTWAY_PUBLIC_ORIGINS", "WebUi__PublicOrigins__0", "WebUi:PublicOrigins[]"),
        ("PORTWAY_SEED_PASSWORD", "WebUi__SeedPassword", "WebUi:SeedPassword"),
        ("PORTWAY_PROMO_TEXT", "WebUi__Customization__PromoText", "WebUi:Customization:PromoText"),
        ("PORTWAY_LOGIN_FOOTER", "WebUi__Customization__LoginFooter", "WebUi:Customization:LoginFooter"),
        ("PORTWAY_KNOWN_PROXIES", "ForwardedHeaders__KnownProxies__0", "ForwardedHeaders:KnownProxies[]"),
        ("PORTWAY_KNOWN_NETWORKS", "ForwardedHeaders__KnownNetworks__0", "ForwardedHeaders:KnownNetworks[]"),
        ("PORTWAY_OIDC_ENABLED", "Oidc__Enabled", "Oidc:Enabled"),
        ("PORTWAY_MCP_ENABLED", "Mcp__Enabled", "Mcp:Enabled"),
        ("PORTWAY_CHAT_ENABLED", "Mcp__ChatEnabled", "Mcp:ChatEnabled"),
        ("PORTWAY_TELEMETRY_PROVIDER", "Telemetry__Provider", "Telemetry:Provider"),
        ("PORTWAY_OTLP_ENDPOINT", "Telemetry__Otlp__Endpoint", "Telemetry:Otlp:Endpoint"),
        ("PORTWAY_SERVICE_NAME", "Telemetry__ServiceName", "Telemetry:ServiceName"),
        ("PORTWAY_USE_HTTPS", "Use_HTTPS", null),
        ("PORTWAY_PROXY_USERNAME", "PROXY_USERNAME", null),
        ("PORTWAY_PROXY_PASSWORD", "PROXY_PASSWORD", null),
        ("PORTWAY_PROXY_DOMAIN", "PROXY_DOMAIN", null),
        ("PORTWAY_KEYVAULT_URI", "KEYVAULT_URI", null),
    ];

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
            if (configKey is null || readEnv(newName) is not { } value) continue;
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
