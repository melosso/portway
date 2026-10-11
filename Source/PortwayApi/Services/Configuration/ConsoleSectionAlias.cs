namespace PortwayApi.Services.Configuration;

using Serilog;

/// <summary>
/// Reads the deprecated WebUi section as Console with the same source precedence
/// </summary>
public static class ConsoleSectionAlias
{
    public const string Section = "Console";

    public const string LegacySection = "WebUi";

    /// <summary>
    /// The Console key for a WebUi key; other keys are returned unchanged
    /// </summary>
    public static string Canonical(string key) =>
        IsUnder(key, LegacySection) ? Section + key[LegacySection.Length..] : key;

    /// <summary>
    /// Adds the provider that resolves Console keys from the highest source holding either name
    /// </summary>
    public static ConfigurationManager AddConsoleSectionAlias(this ConfigurationManager config)
    {
        ((IConfigurationBuilder)config).Add(new AliasSource(config));
        return config;
    }

    public static void WarnOnLegacySection(IConfigurationRoot root)
    {
        if (root.Providers.Any(p => p.GetChildKeys([], LegacySection).Any()))
            Log.Warning("Configuration section {Legacy} is deprecated, rename it to {Section}", LegacySection, Section);
    }

    private static bool IsUnder(string key, string section) =>
        key.StartsWith(section, StringComparison.OrdinalIgnoreCase) &&
        (key.Length == section.Length || key[section.Length] == ':');

    private sealed class AliasSource(IConfigurationRoot root) : IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder) => new AliasProvider(root);
    }

    private sealed class AliasProvider(IConfigurationRoot root) : ConfigurationProvider
    {
        public override bool TryGet(string key, out string? value)
        {
            value = null;
            if (!IsUnder(key, Section)) return false;

            var legacy = LegacySection + key[Section.Length..];
            foreach (var provider in root.Providers.Reverse())
            {
                if (ReferenceEquals(provider, this)) continue;
                if (provider.TryGet(key, out value) || provider.TryGet(legacy, out value)) return true;
            }
            return false;
        }

        public override IEnumerable<string> GetChildKeys(IEnumerable<string> earlierKeys, string? parentPath)
        {
            var others = root.Providers.Where(p => !ReferenceEquals(p, this));

            if (parentPath is null)
                return others.Any(p => p.GetChildKeys([], LegacySection).Any()) ? earlierKeys.Append(Section) : earlierKeys;

            if (!IsUnder(parentPath, Section)) return earlierKeys;

            var legacy = LegacySection + parentPath[Section.Length..];
            return others.Aggregate(earlierKeys, (keys, p) => p.GetChildKeys(keys, legacy));
        }
    }
}
