namespace PortwayApi.Classes.OpenApi;

using Microsoft.OpenApi;

/// <summary>
/// The one place document tags are declared, so every endpoint type names, labels and describes its tag the same way
/// </summary>
internal static class OpenApiTags
{
    /// <summary>
    /// Group for file endpoints without a namespace, matching their /files route segment
    /// </summary>
    public const string FilesGroup = "Files";

    /// <summary>
    /// Declares the endpoint's tag and its namespace label, returns the declared name operations must reference
    /// </summary>
    public static string Declare(OpenApiDocument document, EndpointDefinition definition, string? name = null, string? fallbackDescription = null)
    {
        var tag = Ensure(document, name ?? definition.DocumentationTag, definition.Documentation?.TagDescription ?? fallbackDescription);
        if (!string.IsNullOrWhiteSpace(definition.DisplayName))
        {
            tag.Summary ??= definition.DisplayName;
        }

        if (definition.HasNamespace && !string.IsNullOrWhiteSpace(definition.NamespaceDisplayName))
        {
            // the ordinal first label wins so load order cannot change the title
            var group = Ensure(document, definition.EffectiveNamespace!);
            if (group.Summary is null || string.CompareOrdinal(definition.NamespaceDisplayName, group.Summary) < 0)
            {
                group.Summary = definition.NamespaceDisplayName;
            }
        }

        return tag.Name!;
    }

    /// <summary>
    /// Documented namespaces whose endpoints set different NamespaceDisplayName values, labels in ordinal order
    /// </summary>
    public static IEnumerable<(string Namespace, IReadOnlyList<string> Labels)> NamespaceLabelConflicts(IEnumerable<EndpointDefinition> endpoints) =>
        endpoints
            .Where(e => OpenApiEndpointCatalog.IsDocumented(e) && e.HasNamespace && !string.IsNullOrWhiteSpace(e.NamespaceDisplayName))
            .GroupBy(e => e.EffectiveNamespace!, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Namespace: g.Key, Labels: (IReadOnlyList<string>)g.Select(e => e.NamespaceDisplayName!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList()))
            .Where(c => c.Labels.Count > 1);

    public static OpenApiTag Ensure(OpenApiDocument document, string name, string? description = null)
    {
        document.Tags ??= new HashSet<OpenApiTag>();

        var tag = document.Tags.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        if (tag is null)
        {
            tag = new OpenApiTag { Name = name };
            document.Tags.Add(tag);
        }

        if (string.IsNullOrWhiteSpace(tag.Description) && !string.IsNullOrWhiteSpace(description))
        {
            tag.Description = description;
        }

        return tag;
    }
}
