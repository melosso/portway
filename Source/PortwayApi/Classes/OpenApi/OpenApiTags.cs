namespace PortwayApi.Classes.OpenApi;

using Microsoft.OpenApi;

/// <summary>
/// The one place document tags are declared, so every endpoint type names, labels, describes and groups its tag the same way
/// </summary>
internal static class OpenApiTags
{
    /// <summary>
    /// Group for file endpoints without a namespace, matching their /files route segment
    /// </summary>
    public const string FilesGroup = "Files";

    // groups get their own prefix so no endpoint route can share a group's tag name
    private const string GroupPrefix = "ns:";

    // the registered tag kind for navigation-only tags, set on groups and never on an endpoint tag
    private const string GroupKind = "nav";

    /// <summary>
    /// Tag name for an endpoint, its route path below /api/{env}
    /// </summary>
    public static string NameFor(EndpointDefinition definition) => definition.FullPath;

    /// <summary>
    /// Tag name for the group of a namespace path or a default group
    /// </summary>
    public static string GroupName(string path) => GroupPrefix + path;

    /// <summary>
    /// Group path a group tag stands for, null for an endpoint tag
    /// </summary>
    public static string? GroupPath(string? tagName) =>
        tagName is not null && tagName.StartsWith(GroupPrefix, StringComparison.Ordinal) ? tagName[GroupPrefix.Length..] : null;

    /// <summary>
    /// Declares the endpoint's tag and, when namespaces are shown, its group chain; returns the declared name operations must reference
    /// </summary>
    public static string Declare(OpenApiDocument document, EndpointDefinition definition, string name, OpenApiSettings settings, string? flatGroup = null, string? fallbackDescription = null)
    {
        var tag = Ensure(document, name, definition.Documentation?.TagDescription ?? fallbackDescription);
        tag.Summary ??= string.IsNullOrWhiteSpace(definition.DisplayName) ? definition.EndpointName : definition.DisplayName;

        if (!settings.ShowNamespaces)
        {
            return tag.Name!;
        }

        if (definition.HasNamespace)
        {
            // the ordinal first value wins so load order cannot change the group
            var group = DeclareGroup(document, definition.EffectiveNamespace!);
            group.Summary = OrdinalFirst(group.Summary, definition.NamespaceDisplayName);
            group.Description = OrdinalFirst(group.Description, definition.NamespaceDescription);
            tag.Parent ??= new OpenApiTagReference(group.Name!);
        }
        else if ((flatGroup ?? settings.DefaultGroup)?.Trim() is { Length: > 0 } groupPath)
        {
            tag.Parent ??= new OpenApiTagReference(DeclareGroup(document, groupPath).Name!);
        }

        return tag.Name!;
    }

    /// <summary>
    /// Documented namespaces whose endpoints set different values for one namespace setting, values in ordinal order
    /// </summary>
    public static IEnumerable<(string Namespace, IReadOnlyList<string> Values)> NamespaceConflicts(IEnumerable<EndpointDefinition> endpoints, Func<EndpointDefinition, string?> setting) =>
        endpoints
            .Where(e => OpenApiEndpointCatalog.IsDocumented(e) && e.HasNamespace && !string.IsNullOrWhiteSpace(setting(e)))
            .GroupBy(e => e.EffectiveNamespace!, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Namespace: g.Key, Values: (IReadOnlyList<string>)g.Select(e => setting(e)!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList()))
            .Where(c => c.Values.Count > 1);

    private static string? OrdinalFirst(string? current, string? candidate) =>
        string.IsNullOrWhiteSpace(candidate) || (current is not null && string.CompareOrdinal(current, candidate) <= 0) ? current : candidate;

    // a nested namespace links to the group one segment up, created when no endpoint declared it
    private static OpenApiTag DeclareGroup(OpenApiDocument document, string path)
    {
        var group = Ensure(document, GroupName(path));
        group.Kind ??= GroupKind;

        var cut = path.LastIndexOf('/');
        if (cut > 0 && group.Parent is null)
        {
            group.Parent = new OpenApiTagReference(DeclareGroup(document, path[..cut]).Name!);
        }

        return group;
    }

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
