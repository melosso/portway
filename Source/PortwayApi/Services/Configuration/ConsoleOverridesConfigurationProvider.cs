namespace PortwayApi.Services.Configuration;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using PortwayApi.Helpers;
using Serilog;

/// <summary>
/// Source for appsettings.overrides.json.
/// </summary>
public sealed class ConsoleOverridesConfigurationSource : JsonConfigurationSource
{
    internal Func<string, string?> Decrypt { get; init; } = DecryptOrNull;

    public override IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        EnsureDefaults(builder);
        return new ConsoleOverridesConfigurationProvider(this);
    }

    private static string? DecryptOrNull(string value) =>
        SettingsEncryptionHelper.TryDecryptValue(value, out var plain) ? plain : null;
}

/// <summary>
/// Decrypts values, replaces console collections.
/// </summary>
public sealed class ConsoleOverridesConfigurationProvider(ConsoleOverridesConfigurationSource source)
    : JsonConfigurationProvider(source)
{
    private static readonly HashSet<string> ReplacedPaths =
        new(SettingsWriteService.Schema.Where(s => SettingsWriteService.IsCollection(s.Kind)).Select(s => s.Key),
            StringComparer.OrdinalIgnoreCase);

    public override void Load(Stream stream)
    {
        base.Load(stream);

        foreach (var key in Data.Keys.ToList())
        {
            if (Data[key] is not { } value || !SettingsEncryptionHelper.IsEncrypted(value)) continue;

            Data[key] = source.Decrypt(value);
            if (Data[key] is null)
                Log.Warning("{Key} in {File} could not be decrypted and is ignored", key, SettingsWriteService.OverridesFileName);
        }
    }

    public override IEnumerable<string> GetChildKeys(IEnumerable<string> earlierKeys, string? parentPath)
    {
        if (parentPath is not null && Replaces(parentPath))
            earlierKeys = [];

        return base.GetChildKeys(earlierKeys, parentPath);
    }

    internal bool Replaces(string path) => ReplacedPaths.Contains(path) && Declares(path);

    private bool Declares(string path)
    {
        var prefix = path + ConfigurationPath.KeyDelimiter;
        return Data.Keys.Any(k => k.Equals(path, StringComparison.OrdinalIgnoreCase) || k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}
