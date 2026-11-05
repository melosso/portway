namespace PortwayApi.Services.Configuration;

using System.Collections.Frozen;
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
    public static readonly IReadOnlyList<string> RemovedKeys = ["Mcp:RequireAuthentication", "Mcp:AppsEnabled"];

    private static readonly FrozenDictionary<string, string> CurrentByDeprecated =
        RenamedKeys.ToFrozenDictionary(r => r.Deprecated, r => r.Current, StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, string> DeprecatedByCurrent =
        RenamedKeys.ToFrozenDictionary(r => r.Current, r => r.Deprecated, StringComparer.OrdinalIgnoreCase);

    // names read per renamed key
    private static readonly FrozenDictionary<string, string[]> RenamedNames =
        RenamedKeys.ToFrozenDictionary(r => r.Current, r => (string[])[.. Names(r.Current), .. Names(r.Deprecated)], StringComparer.OrdinalIgnoreCase);

    private static readonly (string Current, string[] DeprecatedNames)[] RenamedParents =
        [.. RenamedKeys.Select(r => (r.Current, Names(r.Deprecated)))];

    /// <summary>
    /// Current name of a key.
    /// </summary>
    public static string Canonical(string key)
    {
        if (IsUnder(key, LegacySection)) key = Section + key[LegacySection.Length..];
        return CurrentByDeprecated.GetValueOrDefault(key, key);
    }

    /// <summary>
    /// Deprecated name, or null.
    /// </summary>
    public static string? Deprecated(string key) => DeprecatedByCurrent.GetValueOrDefault(key);

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
    private static string[] Names(string key) =>
        IsUnder(key, Section) ? [key, LegacySection + key[Section.Length..]] : [key];

    private sealed class AliasSource(IConfigurationRoot root) : IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder) => new AliasProvider(root);
    }

    private sealed class AliasProvider(IConfigurationRoot root) : ConfigurationProvider
    {
        public override bool TryGet(string key, out string? value)
        {
            if (RenamedNames.TryGetValue(key, out var names)) return TryRead(names, out value);

            value = null;
            return IsUnder(key, Section) && TryRead([key, LegacySection + key[Section.Length..]], out value);
        }

        public override IEnumerable<string> GetChildKeys(IEnumerable<string> earlierKeys, string? parentPath)
        {
            var providers = Providers();
            var keys = earlierKeys;

            if (parentPath is not null && IsUnder(parentPath, Section))
            {
                var legacy = LegacySection + parentPath[Section.Length..];
                IEnumerable<string> legacyKeys = [];
                foreach (var provider in providers)
                {
                    if (ReferenceEquals(provider, this)) continue;
                    // replaced lists hide legacy layers
                    if (provider is ConsoleOverridesConfigurationProvider overrides && overrides.Replaces(parentPath))
                        legacyKeys = [];
                    legacyKeys = provider.GetChildKeys(legacyKeys, legacy);
                }
                keys = keys.Concat(legacyKeys);
            }

            var result = new List<string>(keys);

            if (parentPath is null && HasLegacySection(providers))
                result.Add(Section);

            // Renamed keys under new parent
            var prefix = parentPath is null ? "" : parentPath + ":";
            foreach (var (current, deprecatedNames) in RenamedParents)
            {
                if (current.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && TryRead(deprecatedNames, out _))
                    result.Add(current[prefix.Length..].Split(':')[0]);
            }

            result.Sort(ConfigurationKeyComparer.Instance);
            return result;
        }

        private IList<IConfigurationProvider> Providers() =>
            root.Providers as IList<IConfigurationProvider> ?? [.. root.Providers];

        private bool HasLegacySection(IList<IConfigurationProvider> providers)
        {
            foreach (var provider in providers)
            {
                if (ReferenceEquals(provider, this)) continue;
                foreach (var _ in provider.GetChildKeys([], LegacySection)) return true;
            }
            return false;
        }

        private bool TryRead(ReadOnlySpan<string> names, out string? value)
        {
            var providers = Providers();
            for (var i = providers.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(providers[i], this)) continue;
                foreach (var name in names)
                    if (providers[i].TryGet(name, out value)) return true;
            }
            value = null;
            return false;
        }
    }
}
