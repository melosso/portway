namespace PortwayApi.Classes.OpenApi;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

/// <summary>Turns slash-delimited namespace tags into an OpenAPI 3.2 tag hierarchy so nested namespaces render as a tree</summary>
public class HierarchicalTagDocumentFilter : IOpenApiDocumentTransformer
{
    // "nav" is the registered tag-kind for navigation grouping; renderers key their sidebar tree off it
    private const string NamespaceTagKind = "nav";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        if (document.Tags is null || document.Tags.Count == 0)
        {
            return Task.CompletedTask;
        }

        // Index existing tags by name so parent segments can be reused
        var byName = new Dictionary<string, OpenApiTag>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in document.Tags)
        {
            if (!string.IsNullOrEmpty(tag.Name))
            {
                byName[tag.Name] = tag;
            }
        }

        // Create every missing ancestor first, so tags nested more than one level deep can find their own parent below
        foreach (var tag in document.Tags.ToList())
        {
            var segments = Segments(tag.Name);
            for (int i = 1; i < segments.Length; i++)
            {
                var ancestor = string.Join('/', segments.Take(i));
                if (!byName.ContainsKey(ancestor))
                {
                    var created = new OpenApiTag { Name = ancestor };
                    document.Tags.Add(created);
                    byName[ancestor] = created;
                }
            }
        }

        // Link every namespaced tag to its immediate parent segment
        foreach (var tag in document.Tags)
        {
            var segments = Segments(tag.Name);
            if (segments.Length < 2)
            {
                continue;
            }

            tag.Parent = new OpenApiTagReference(string.Join('/', segments.Take(segments.Length - 1)));
            tag.Kind ??= NamespaceTagKind;

            // The name carries the path so the hierarchy survives; summary carries the leaf a reader should see
            tag.Summary ??= segments[^1];
        }

        return Task.CompletedTask;
    }

    private static string[] Segments(string? tagName) =>
        string.IsNullOrEmpty(tagName) ? [] : tagName.Split('/', StringSplitOptions.RemoveEmptyEntries);
}
