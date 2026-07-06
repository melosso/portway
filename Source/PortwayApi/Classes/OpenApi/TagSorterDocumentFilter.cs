using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using System.Diagnostics;

namespace PortwayApi.Classes.OpenApi;

public class TagSorterDocumentFilter : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        // Sort all tags alphabetically
        if (document.Tags != null && document.Tags.Count > 0)
        {
            // Log the tags before sorting for debugging
            var tagsBefore = string.Join(", ", document.Tags.Select(t => t.Name));
            Debug.WriteLine($"TagSorterDocumentFilter - Tags before sorting: {tagsBefore}");

            var sortedTags = document.Tags
                .OrderBy(tag => tag.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            document.Tags.Clear();
            foreach (var tag in sortedTags)
            {
                document.Tags.Add(tag);
            }

            // Log the tags after sorting for debugging
            var tagsAfter = string.Join(", ", document.Tags.Select(t => t.Name));
            Debug.WriteLine($"TagSorterDocumentFilter - Tags after sorting: {tagsAfter}");
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
}
