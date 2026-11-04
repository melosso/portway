namespace PortwayApi.Services.Configuration;

using Serilog;

/// <summary>
/// Resolves deprecated configuration names.
/// </summary>
public static class ConfigAliases
{
    public const string Section = "Console";

    public const string LegacySection = "WebUi";

    /// <summary>
    /// Renamed keys as (current, deprecated).
    /// </summary>
    public static readonly IReadOnlyList<(string Current, string Deprecated)> RenamedKeys =
    [
        ("RateLimiting:IpWindowSeconds", "RateLimiting:IpWindow"),
        ("RateLimiting:TokenWindowSeconds", "RateLimiting:TokenWindow"),
        ("RateLimiting:Provider", "RateLimiting:Store"),
        ("RateLimiting:Redis:ConnectionString", "RateLimiting:RedisConnectionString"),
        ("SqlConnectionPooling:ConnectionTimeoutSeconds", "SqlConnectionPooling:ConnectionTimeout"),
        ("SqlConnectionPooling:CommandTimeoutSeconds", "SqlConnectionPooling:CommandTimeout"),
        ("Caching:Provider", "Caching:ProviderType"),
        ("FileStorage:MemoryCacheDurationSeconds", "FileStorage:MemoryCacheTimeSeconds"),
        ("RequestTrafficLogging:Provider", "RequestTrafficLogging:StorageType"),
        ("RequestTrafficLogging:CaptureRequestBodies", "RequestTrafficLogging:IncludeRequestBodies"),
        ("RequestTrafficLogging:CaptureResponseBodies", "RequestTrafficLogging:IncludeResponseBodies"),
        ("RequestTrafficLogging:InfoLoggingEnabled", "RequestTrafficLogging:EnableInfoLogging"),
        ("Console:Customization:LandingPageEnabled", "Console:Customization:EnableLandingPage"),
        ("OpenApi:Scalar:Theme", "OpenApi:ScalarTheme"),
        ("OpenApi:Scalar:Layout", "OpenApi:ScalarLayout"),
        ("OpenApi:Scalar:ShowSidebar", "OpenApi:ScalarShowSidebar"),
        ("OpenApi:Scalar:HideDownloadButton", "OpenApi:ScalarHideDownloadButton"),
        ("OpenApi:Scalar:HideModels", "OpenApi:ScalarHideModels"),
        ("OpenApi:Scalar:HideClientButton", "OpenApi:ScalarHideClientButton"),
        ("OpenApi:Scalar:HideTestRequestButton", "OpenApi:ScalarHideTestRequestButton"),
    ];

    /// <summary>
    /// Keys that no longer apply.
    /// </summary>
    public static readonly IReadOnlyList<string> RemovedKeys = ["Mcp:RequireAuthentication"];

    /// <summary>
    /// Current name of a key.
    /// </summary>
    public static string Canonical(string key)
    {
        if (IsUnder(key, LegacySection)) key = Section + key[LegacySection.Length..];
        return RenamedKeys.FirstOrDefault(r => r.Deprecated.Equals(key, StringComparison.OrdinalIgnoreCase)).Current ?? key;
    }

    /// <summary>
    /// Deprecated name, or null.
    /// </summary>
    public static string? Deprecated(string key) =>
        RenamedKeys.FirstOrDefault(r => r.Current.Equals(key, StringComparison.OrdinalIgnoreCase)).Deprecated;

    /// <summary>
    /// Adds the alias provider.
    /// </summary>
    public static ConfigurationManager AddConfigAliases(this ConfigurationManager config)
    {
        ((IConfigurationBuilder)config).Add(new AliasSource(config));
        return config;
    }

    public static void WarnOnDeprecatedNames(IConfigurationRoot root)
    {
        var providers = root.Providers.Where(p => p is not AliasProvider).ToList();

        if (providers.Any(p => p.GetChildKeys([], LegacySection).Any()))
            Log.Warning("Configuration section {Legacy} is deprecated, rename it to {Section}", LegacySection, Section);

        foreach (var (current, deprecated) in RenamedKeys)
        {
            if (Names(deprecated).Any(name => providers.Any(p => p.TryGet(name, out _))))
                Log.Warning("Configuration key {Deprecated} is deprecated, rename it to {Current}", deprecated, current);
        }
    }

    private static bool IsUnder(string key, string section) =>
        key.StartsWith(section, StringComparison.OrdinalIgnoreCase) &&
        (key.Length == section.Length || key[section.Length] == ':');

    // WebUi also names Console
    private static IEnumerable<string> Names(string key) =>
        IsUnder(key, Section) ? [key, LegacySection + key[Section.Length..]] : [key];

    private sealed class AliasSource(IConfigurationRoot root) : IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder) => new AliasProvider(root);
    }

    private sealed class AliasProvider(IConfigurationRoot root) : ConfigurationProvider
    {
        private IEnumerable<IConfigurationProvider> Others => root.Providers.Where(p => !ReferenceEquals(p, this));

        public override bool TryGet(string key, out string? value)
        {
            value = null;
            var deprecated = Deprecated(key);
            if (!IsUnder(key, Section) && deprecated is null) return false;

            var names = Names(key).Concat(deprecated is null ? [] : Names(deprecated)).ToArray();
            foreach (var provider in Others.Reverse())
            {
                foreach (var name in names)
                    if (provider.TryGet(name, out value)) return true;
            }
            return false;
        }

        public override IEnumerable<string> GetChildKeys(IEnumerable<string> earlierKeys, string? parentPath)
        {
            var keys = earlierKeys;

            if (parentPath is null && Others.Any(p => p.GetChildKeys([], LegacySection).Any()))
                keys = keys.Append(Section);

            if (parentPath is not null && IsUnder(parentPath, Section))
            {
                var legacy = LegacySection + parentPath[Section.Length..];
                keys = Others.Aggregate(keys, (k, p) => p.GetChildKeys(k, legacy));
            }

            // Renamed keys under new parent
            var prefix = parentPath is null ? "" : parentPath + ":";
            foreach (var (current, deprecated) in RenamedKeys)
            {
                if (!current.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (!Names(deprecated).Any(name => Others.Any(p => p.TryGet(name, out _)))) continue;
                keys = keys.Append(current[prefix.Length..].Split(':')[0]);
            }

            return keys;
        }
    }
}
