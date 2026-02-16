using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace PortwayApi.Classes.Swagger;

public class TagSorterDocumentFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        // Sort all tags alphabetically
        if (swaggerDoc.Tags != null && swaggerDoc.Tags.Count > 0)
        {
            // Log the tags before sorting for debugging
            var tagsBefore = string.Join(", ", swaggerDoc.Tags.Select(t => t.Name));
            Debug.WriteLine($"TagSorterDocumentFilter - Tags before sorting: {tagsBefore}");
            
            var sortedTags = swaggerDoc.Tags
                .OrderBy(tag => tag.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            
            swaggerDoc.Tags.Clear();
            foreach (var tag in sortedTags)
            {
                swaggerDoc.Tags.Add(tag);
            }
            
            // Log the tags after sorting for debugging
            var tagsAfter = string.Join(", ", swaggerDoc.Tags.Select(t => t.Name));
            Debug.WriteLine($"TagSorterDocumentFilter - Tags after sorting: {tagsAfter}");
        }
        else
        {
            Debug.WriteLine("TagSorterDocumentFilter - No tags found to sort");
        }

        // Sort all paths alphabetically
        if (swaggerDoc.Paths != null && swaggerDoc.Paths.Count > 0)
        {
            // Log the paths before sorting for debugging
            var pathsBefore = string.Join(", ", swaggerDoc.Paths.Keys.Take(10)); // Only first 10 for brevity
            Debug.WriteLine($"TagSorterDocumentFilter - Paths before sorting (first 10): {pathsBefore}");
            
            // Create a new sorted dictionary
            var sortedPaths = new OpenApiPaths();
            var orderedPaths = swaggerDoc.Paths
                .OrderBy(path => path.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();
            
            foreach (var path in orderedPaths)
            {
                sortedPaths.Add(path.Key, path.Value);
            }
            
            // Replace the paths with the sorted version
            swaggerDoc.Paths = sortedPaths;
            
            // Log the paths after sorting for debugging
            var pathsAfter = string.Join(", ", swaggerDoc.Paths.Keys.Take(10)); // Only first 10 for brevity
            Debug.WriteLine($"TagSorterDocumentFilter - Paths after sorting (first 10): {pathsAfter}");
        }
        else
        {
            Debug.WriteLine("TagSorterDocumentFilter - No paths found to sort");
        }
    }
}
