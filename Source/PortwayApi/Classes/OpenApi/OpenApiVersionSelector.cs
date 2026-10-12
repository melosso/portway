namespace PortwayApi.Classes.OpenApi;

using Microsoft.OpenApi;

/// <summary>
/// Narrows a generated document to the operations of one endpoint version
/// </summary>
internal static class OpenApiVersionSelector
{
    /// <summary>
    /// Endpoint versions in use, ordered by number, v1 always first
    /// </summary>
    public static IReadOnlyList<string> Versions() =>
        OpenApiEndpointCatalog.All()
            .Where(e => OpenApiEndpointCatalog.IsDocumented(e.Definition))
            .Select(e => e.Definition.Version ?? EndpointVersion.Default)
            .Append(EndpointVersion.Default)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(EndpointVersion.Number)
            .ToList();

    /// <summary>
    /// Removes the paths and tags of other versions; false when the version has no operations
    /// </summary>
    public static bool Select(OpenApiDocument document, string version)
    {
        var bases = OpenApiEndpointCatalog.All()
            .Where(e => EndpointVersion.Number(e.Definition.Version) == EndpointVersion.Number(version))
            .Select(e => e.BasePath)
            .ToList();

        foreach (var pathKey in (document.Paths?.Keys ?? Enumerable.Empty<string>()).ToList())
        {
            if (!bases.Any(b => OpenApiEndpointCatalog.Covers(b, pathKey)))
            {
                document.Paths!.Remove(pathKey);
            }
        }

        if (document.Paths is not { Count: > 0 })
        {
            return false;
        }

        var tags = document.Tags?.ToList() ?? [];
        var parents = tags.Where(t => t.Name is not null).ToDictionary(t => t.Name!, t => t.Parent?.Name, StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var operationTags = document.Paths.Values
            .SelectMany(p => p.Operations?.Values ?? Enumerable.Empty<OpenApiOperation>())
            .SelectMany(o => o.Tags ?? Enumerable.Empty<OpenApiTagReference>())
            .Select(t => t.Name);

        foreach (var name in operationTags)
        {
            var current = name;
            while (current is not null && used.Add(current))
            {
                current = parents.GetValueOrDefault(current);
            }
        }

        foreach (var tag in tags.Where(t => t.Name is null || !used.Contains(t.Name)))
        {
            document.Tags!.Remove(tag);
        }

        return true;
    }
}
