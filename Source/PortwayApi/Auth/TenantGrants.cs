using System.Collections.Frozen;
using System.Text.Json;
using System.Text.RegularExpressions;
using PortwayApi.Helpers;

namespace PortwayApi.Auth;

/// <summary>
/// Parsing, validation and serialization of Tokens.AllowedTenants
/// </summary>
public static partial class TenantGrants
{
    public const string Empty = "{}";
    public const string Wildcard = "*";
    public const int MaxHeaderLength = 64;

    public static readonly FrozenDictionary<string, FrozenSet<string>> None =
        FrozenDictionary.Create<string, FrozenSet<string>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Tenant value grammar: [A-Za-z0-9][A-Za-z0-9_.-]{0,63}
    /// </summary>
    public static bool IsValidValue(string? value) => value is not null && TenantValue().IsMatch(value);

    /// <summary>
    /// Valid HTTP token of at most 64 characters, not reserved by HeaderPolicy
    /// </summary>
    public static bool IsValidHeader(string? name) =>
        HeaderPolicy.IsValidName(name) && name!.Length <= MaxHeaderLength && !HeaderPolicy.IsReserved(name);

    public static bool TryParse(string? json, out FrozenDictionary<string, FrozenSet<string>> grants, out string? error)
    {
        grants = None;
        error = null;
        if (string.IsNullOrWhiteSpace(json))
            return true;

        Dictionary<string, List<string>>? raw;
        try
        {
            raw = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);
        }
        catch (JsonException)
        {
            error = "allowed_tenants must be an object of header names to lists of values";
            return false;
        }

        return TryCreate(raw, out grants, out error);
    }

    public static bool TryCreate(IReadOnlyDictionary<string, List<string>>? raw, out FrozenDictionary<string, FrozenSet<string>> grants, out string? error)
    {
        grants = None;
        error = null;
        if (raw is null || raw.Count == 0)
            return true;

        var map = new Dictionary<string, FrozenSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (header, values) in raw)
        {
            if (!IsValidHeader(header))
            {
                error = $"'{header}' cannot be used as a tenant header";
                return false;
            }
            if (map.ContainsKey(header))
            {
                error = $"Tenant header '{header}' is listed twice";
                return false;
            }
            if (values is null || values.Count == 0)
            {
                error = $"Tenant header '{header}' needs at least one value";
                return false;
            }
            var invalid = values.FirstOrDefault(v => v != Wildcard && !IsValidValue(v));
            if (invalid is not null || values.Contains(null!))
            {
                error = $"Tenant header '{header}' has an invalid value";
                return false;
            }
            map[header] = values.ToFrozenSet(StringComparer.Ordinal);
        }

        grants = map.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        return true;
    }

    /// <summary>
    /// Canonical JSON with values sorted ordinally
    /// </summary>
    public static string Serialize(IReadOnlyDictionary<string, FrozenSet<string>> grants) =>
        grants.Count == 0
            ? Empty
            : JsonSerializer.Serialize(grants
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Value.Order(StringComparer.Ordinal).ToArray()));

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$")]
    private static partial Regex TenantValue();
}
