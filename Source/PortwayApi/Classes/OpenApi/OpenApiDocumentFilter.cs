using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Serilog;

namespace PortwayApi.Classes;

/// <summary>Helps fix issues with catch-all route parameters in OpenAPI documentation</summary>
public class OpenApiDocumentFilter : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        // Remove any catch-all route paths that can cause conflicts
        var paths = document.Paths.Keys.Where(p => p.Contains("{**")).ToList();
        foreach (var path in paths)
        {
            document.Paths.Remove(path);
        }

        // Remove any duplicate paths that might exist
        var duplicateKeys = document.Paths.Keys
            .GroupBy(path => path)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        foreach (var key in duplicateKeys)
        {
            document.Paths.Remove(key);
        }

        return Task.CompletedTask;
    }
}
