using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using System.Diagnostics;

namespace PortwayApi.Classes.OpenApi;

public class TagSorterDocumentFilter : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        if (document.Tags != null && document.Tags.Count > 0)
        {
            var sortedTags = SortTree(document.Tags);
            document.Tags.Clear();
            foreach (var tag in sortedTags)
            {
                document.Tags.Add(tag);
            }
        }
        else
        {
            Debug.WriteLine("TagSorterDocumentFilter - No tags found to sort");
        }

        // Sort all paths alphabetically
        if (document.Paths != null && document.Paths.Count > 0)
        {
            // Log the paths before sorting for debugging
            var pathsBefore = string.Join(", ", document.Paths.Keys.Take(10)); // Only first 10 for brevity
            Debug.WriteLine($"TagSorterDocumentFilter - Paths before sorting (first 10): {pathsBefore}");

            // Create a new sorted dictionary
            var sortedPaths = new OpenApiPaths();
            var orderedPaths = document.Paths
                .OrderBy(path => path.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var path in orderedPaths)
            {
                sortedPaths.Add(path.Key, path.Value);
            }

            // Replace the paths with the sorted version
            document.Paths = sortedPaths;

            // Log the paths after sorting for debugging
            var pathsAfter = string.Join(", ", document.Paths.Keys.Take(10)); // Only first 10 for brevity
            Debug.WriteLine($"TagSorterDocumentFilter - Paths after sorting (first 10): {pathsAfter}");
        }
        else
        {
            Debug.WriteLine("TagSorterDocumentFilter - No paths found to sort");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Depth first per group, endpoints before subgroups, siblings by the title the sidebar shows
    /// </summary>
    internal static List<OpenApiTag> SortTree(IEnumerable<OpenApiTag> tags)
    {
        var all = tags.ToList();
        var names = all.Select(t => t.Name).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var children = all.ToLookup(t => t.Parent?.Name is { } parent && names.Contains(parent) ? parent : string.Empty, StringComparer.OrdinalIgnoreCase);

        var sorted = new List<OpenApiTag>(all.Count);
        void Add(string parent)
        {
            foreach (var tag in children[parent]
                .OrderBy(t => children.Contains(t.Name ?? string.Empty))
                .ThenBy(t => t.Summary ?? t.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.Name, StringComparer.Ordinal))
            {
                sorted.Add(tag);
                if (!string.IsNullOrEmpty(tag.Name))
                {
                    Add(tag.Name);
                }
            }
        }

        Add(string.Empty);
        return sorted;
    }
}
