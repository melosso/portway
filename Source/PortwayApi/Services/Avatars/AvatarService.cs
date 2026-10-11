namespace PortwayApi.Services.Avatars;

using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DiceBear;
using PortwayApi.Auth;
using Serilog;

/// <summary>
/// DiceBear avatars for console accounts, rendered from a stored style and seed
/// </summary>
public sealed partial class AvatarService
{
    private const int MaxCached = 512;
    private const string LegacyStyle = "shapes";

    // Only CC0 styles are offered so no rendered avatar carries an attribution duty; initials renders the seed as text
    private static readonly IReadOnlyDictionary<string, string> Definitions = LoadDefinitions();
    private static readonly ConcurrentDictionary<string, Style?> Parsed = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.Ordinal);

    /// <summary>
    /// Style keys an account can use, in kebab case
    /// </summary>
    public static IReadOnlyList<string> Styles { get; } = Definitions.Keys.Order(StringComparer.Ordinal).ToList();

    [GeneratedRegex("^[A-Za-z0-9_.-]{1,64}$")]
    private static partial Regex SeedPattern();

    public static bool IsKnownStyle(string? style) => style is not null && Definitions.ContainsKey(style);

    public static bool IsValidSeed(string? seed) => seed is not null && SeedPattern().IsMatch(seed);

    /// <summary>
    /// A random offered style with a random seed
    /// </summary>
    public static (string Style, string Seed) Random() =>
        (Styles[RandomNumberGenerator.GetInt32(Styles.Count)], Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6)));

    /// <summary>
    /// The account's avatar as a data URI, or null when it was removed or cannot be rendered
    /// </summary>
    public string? For(AdminUser user) => user.AvatarStyle switch
    {
        null => Render(LegacyStyle, user.Username),
        "" => null,
        var style => Render(style, user.AvatarSeed ?? user.Username)
    };

    /// <summary>
    /// A data URI for an offered style and a valid seed, otherwise null
    /// </summary>
    public string? Render(string style, string seed)
    {
        if (!IsKnownStyle(style) || !IsValidSeed(seed)) return null;

        var key = $"{style}:{seed}";
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var parsed = Parsed.GetOrAdd(style, ParseStyle);
        if (parsed is null) return null;

        try
        {
            var uri = new Avatar(parsed, new JsonObject { ["seed"] = seed }).ToDataUri();

            // bounded without eviction, past the cap a preview renders on every call
            if (_cache.Count < MaxCached) _cache[key] = uri;
            return uri;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not render a {Style} avatar", style);
            return null;
        }
    }

    /// <summary>
    /// Parses every offered style so the first picker request does not pay for it
    /// </summary>
    public static void Warm(CancellationToken ct)
    {
        foreach (var style in Styles)
        {
            if (ct.IsCancellationRequested) return;
            Parsed.GetOrAdd(style, ParseStyle);
        }
    }

    private static Style? ParseStyle(string style)
    {
        try
        {
            return Style.Parse(Definitions[style]);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not load the DiceBear {Style} style", style);
            return null;
        }
    }

    private static IReadOnlyDictionary<string, string> LoadDefinitions()
    {
        var definitions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in typeof(DiceBear.Styles).GetProperties(BindingFlags.Public | BindingFlags.Static))
        {
            if (property.PropertyType != typeof(string) || property.Name == "Initials") continue;
            try
            {
                var json = (string)property.GetValue(null)!;
                if (JsonNode.Parse(json)?["meta"]?["license"]?["name"]?.GetValue<string>() != "CC0 1.0") continue;
                definitions[KebabCase().Replace(property.Name, "$1-$2").ToLowerInvariant()] = json;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Skipping DiceBear style {Style}", property.Name);
            }
        }
        return definitions;
    }

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex KebabCase();
}

/// <summary>
/// Parses the avatar styles in the background after startup
/// </summary>
public sealed class AvatarWarmupService : Microsoft.Extensions.Hosting.BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => AvatarService.Warm(stoppingToken), stoppingToken);
}
