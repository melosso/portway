namespace PortwayApi.Services.Avatars;

using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using DiceBear;
using Serilog;

/// <summary>
/// Identicons for console accounts. The username is the seed, so an account looks the
/// same everywhere and nothing has to be stored against it.
/// </summary>
public sealed class AvatarService
{
    private const int MaxCached = 256;

    // Parsing the style reads and validates a schema; do it once
    private static readonly Lazy<Style?> Shapes = new(() =>
    {
        try
        {
            return Style.Parse(Styles.Shapes);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not load the DiceBear shapes style; accounts fall back to an initial");
            return null;
        }
    });

    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.Ordinal);

    /// <summary>A data URI for the account, or null when the style could not be loaded</summary>
    public string? DataUriFor(string seed)
    {
        if (string.IsNullOrWhiteSpace(seed)) return null;
        if (_cache.TryGetValue(seed, out var cached)) return cached;

        var style = Shapes.Value;
        if (style is null) return null;

        try
        {
            var avatar = new Avatar(style, new JsonObject { ["seed"] = seed });
            var uri = avatar.ToDataUri();

            // A gateway has few accounts; the bound is only there so a rename loop cannot grow this forever
            if (_cache.Count < MaxCached) _cache[seed] = uri;
            return uri;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not render an avatar for {Seed}", seed);
            return null;
        }
    }
}
