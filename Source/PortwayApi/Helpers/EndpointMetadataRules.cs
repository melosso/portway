using System.Text.RegularExpressions;
using PortwayApi.Classes;

namespace PortwayApi.Helpers;

/// <summary>
/// Validation of an endpoint's Stability and Badges
/// </summary>
public static partial class EndpointMetadataRules
{
    private const int MaxBadgeNameLength = 32;

    public static IReadOnlyList<string> Validate(EndpointDefinition endpoint)
    {
        var errors = new List<string>();

        if (endpoint.Stability is { } stability && !IsAny(stability, "Stable", "Experimental"))
            errors.Add("Stability must be Stable or Experimental. Use Deprecated to deprecate an endpoint.");

        var number = 0;
        foreach (var badge in endpoint.Badges ?? [])
        {
            number++;
            if (string.IsNullOrWhiteSpace(badge.Name) || badge.Name.Length > MaxBadgeNameLength)
                errors.Add($"Badge {number}: Name requires 1 to {MaxBadgeNameLength} characters.");
            if (badge.Position is { } position && !IsAny(position, "before", "after"))
                errors.Add($"Badge {number}: Position must be before or after.");
            if (badge.Color is { } color && !CssColor().IsMatch(color))
                errors.Add($"Badge {number}: Color must be a CSS keyword, hex, rgb, rgba, hsl or hsla value.");
        }

        return errors;
    }

    private static bool IsAny(string value, params string[] allowed) =>
        allowed.Contains(value, StringComparer.OrdinalIgnoreCase);

    // vendored scalar x-badges color pattern
    [GeneratedRegex(@"^(#([0-9A-Fa-f]{3}){1,2}|rgb\(\s*\d+\s*,\s*\d+\s*,\s*\d+\s*\)|rgba\(\s*\d+\s*,\s*\d+\s*,\s*\d+\s*,\s*[0-9.]*\s*\)|hsl\(\s*\d+\s*,\s*\d+%\s*,\s*\d+%\s*\)|hsla\(\s*\d+\s*,\s*\d+%\s*,\s*\d+%\s*,\s*[0-9.]*\s*\)|[a-zA-Z]+)\z")]
    private static partial Regex CssColor();
}
