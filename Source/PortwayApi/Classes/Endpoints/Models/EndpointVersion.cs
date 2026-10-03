namespace PortwayApi.Classes;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Endpoint version tokens: v followed by 1 to 999, where v1 is the unversioned endpoint
/// </summary>
public static class EndpointVersion
{
    public const string Default = "v1";

    public static bool TryParse(string? segment, [NotNullWhen(true)] out string? version)
    {
        version = null;
        if (segment is not { Length: >= 2 and <= 4 } || (segment[0] | 0x20) != 'v' || segment[1] == '0')
        {
            return false;
        }

        foreach (var c in segment.AsSpan(1))
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        version = "v" + segment[1..];
        return true;
    }

    public static bool IsDefault(string? version) => version is null || version == Default;

    /// <summary>
    /// Key suffix for a version, empty for the default version
    /// </summary>
    public static string Suffix(string? version) => IsDefault(version) ? "" : "@" + version;

    /// <summary>
    /// Route segment for a version, empty for the default version
    /// </summary>
    public static string RoutePrefix(string? version) => IsDefault(version) ? "" : version + "/";

    /// <summary>
    /// Splits a key such as Inventory/Products@v2 into its base key and version
    /// </summary>
    public static (string Key, string? Version) Split(string key)
    {
        var at = key.LastIndexOf('@');
        return at < 0 ? (key, null) : (key[..at], key[(at + 1)..]);
    }

    /// <summary>
    /// Folder path of a key below the endpoint type folder, Inventory/Products@v2 becomes Inventory/Products/v2
    /// </summary>
    public static string FolderPath(string key)
    {
        var (path, version) = Split(key);
        return version is null ? path : $"{path}/{version}";
    }

    /// <summary>
    /// Route below /api/{env} for a key, Inventory/Products@v2 becomes v2/Inventory/Products
    /// </summary>
    public static string RoutePath(string key)
    {
        var (path, version) = Split(key);
        return RoutePrefix(version) + path;
    }

    /// <summary>
    /// File route below /api/{env} for a file endpoint key, Images@v2 becomes v2/files/Images
    /// </summary>
    public static string FileRoutePath(string key)
    {
        var (path, version) = Split(key);
        return $"{RoutePrefix(version)}files/{path}";
    }

    public static int Number(string? version) => IsDefault(version) ? 1 : int.Parse(version.AsSpan(1));
}
