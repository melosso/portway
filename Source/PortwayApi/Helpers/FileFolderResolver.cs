using System.Text.RegularExpressions;

namespace PortwayApi.Helpers;

/// <summary>
/// Expands a file endpoint's BaseDirectory into upload and read folders
/// </summary>
public static partial class FileFolderResolver
{
    private static readonly string[] TimePlaceholders = ["{date}", "{year}", "{month}"];
    private static readonly string[] BuiltInPlaceholders = ["env", "date", "year", "month"];

    /// <summary>
    /// Upload is the expanded path; Scope ends before the first date placeholder segment and is a full path when BaseDirectory is absolute
    /// </summary>
    public static FileFolders Resolve(string? baseDirectory, string environment, IReadOnlyDictionary<string, string> tenants, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
            return new FileFolders(string.Empty, string.Empty, IsAbsolute: false);
        if (Path.IsPathRooted(baseDirectory))
        {
            var timeAt = TimePlaceholders.Select(p => baseDirectory.IndexOf(p, StringComparison.OrdinalIgnoreCase)).Where(i => i >= 0).DefaultIfEmpty(-1).Min();
            var scopeRaw = timeAt < 0 ? baseDirectory : baseDirectory[..(baseDirectory.LastIndexOfAny(['/', '\\'], timeAt) + 1)];
            return new FileFolders(
                Path.GetFullPath(Expand(baseDirectory, environment, tenants, utcNow)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(Expand(scopeRaw, environment, tenants, utcNow))),
                IsAbsolute: true);
        }

        var upload = new List<string>();
        var scope = new List<string>();
        var timeSeen = false;

        foreach (var raw in baseDirectory.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            timeSeen |= TimePlaceholders.Any(p => raw.Contains(p, StringComparison.OrdinalIgnoreCase));
            var segment = Expand(raw, environment, tenants, utcNow);
            upload.Add(segment);
            if (!timeSeen)
                scope.Add(segment);
        }

        var folders = new FileFolders(string.Join('/', upload), string.Join('/', scope), IsAbsolute: false);
        if ((folders.Upload.Length > 0 && !Services.Files.FileHandlerService.IsSafeRelativePath(folders.Upload))
            || (folders.Scope.Length > 0 && !Services.Files.FileHandlerService.IsSafeRelativePath(folders.Scope)))
            throw new ArgumentException("BaseDirectory resolves to an unsafe path");
        return folders;
    }

    private static string Expand(string value, string environment, IReadOnlyDictionary<string, string> tenants, DateTime utcNow)
    {
        var expanded = value
            .Replace("{env}", environment, StringComparison.OrdinalIgnoreCase)
            .Replace("{date}", utcNow.ToString("yyyy-MM-dd"), StringComparison.OrdinalIgnoreCase)
            .Replace("{year}", utcNow.Year.ToString("0000"), StringComparison.OrdinalIgnoreCase)
            .Replace("{month}", utcNow.Month.ToString("00"), StringComparison.OrdinalIgnoreCase);
        return Placeholder().Replace(expanded, m => tenants.TryGetValue(m.Groups[1].Value, out var tenant) ? tenant : m.Value);
    }

    /// <summary>
    /// Non-built-in placeholder names in a BaseDirectory
    /// </summary>
    public static IReadOnlyList<string> PlaceholderNames(string? baseDirectory) =>
        string.IsNullOrEmpty(baseDirectory)
            ? []
            : Placeholder().Matches(baseDirectory)
                .Select(m => m.Groups[1].Value)
                .Where(name => !BuiltInPlaceholders.Contains(name, StringComparer.OrdinalIgnoreCase))
                .ToList();

    /// <summary>
    /// Placeholder names at or after the first date placeholder segment
    /// </summary>
    public static IReadOnlyList<string> PlaceholdersAfterTime(string? baseDirectory)
    {
        var after = new List<string>();
        var timeSeen = false;
        foreach (var segment in (baseDirectory ?? string.Empty).Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            timeSeen |= TimePlaceholders.Any(p => segment.Contains(p, StringComparison.OrdinalIgnoreCase));
            if (timeSeen)
                after.AddRange(PlaceholderNames(segment));
        }
        return after;
    }

    [GeneratedRegex(@"\{([A-Za-z0-9!#$%&'*+.^_`|~-]+)\}")]
    private static partial Regex Placeholder();
}

public readonly record struct FileFolders(string Upload, string Scope, bool IsAbsolute);
