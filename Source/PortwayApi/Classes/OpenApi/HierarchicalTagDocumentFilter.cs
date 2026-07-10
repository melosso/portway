namespace PortwayApi.Classes.OpenApi;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

/// <summary>Turns slash-delimited namespace tags into an OpenAPI 3.2 tag hierarchy so nested namespaces render as a tree</summary>
public class HierarchicalTagDocumentFilter : IOpenApiDocumentTransformer
{
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

        foreach (var tag in document.Tags.ToList())
        {
            var name = tag.Name ?? string.Empty;
            if (!name.Contains('/'))
            {
                continue;
            }

            var segments = name.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2)
            {
                continue;
            }

            // Ensure a tag exists for every ancestor segment
            for (int i = 1; i < segments.Length; i++)
            {
                var ancestor = string.Join('/', segments.Take(i));
                if (!byName.ContainsKey(ancestor))
                {
                    var parent = new OpenApiTag { Name = ancestor, Kind = "namespace" };
                    document.Tags.Add(parent);
                    byName[ancestor] = parent;
                }
            }

            // Link this tag to its immediate parent segment
            var parentName = string.Join('/', segments.Take(segments.Length - 1));
            tag.Parent = new OpenApiTagReference(parentName);
            tag.Kind ??= "namespace";
        }

        return Task.CompletedTask;
    }
}
