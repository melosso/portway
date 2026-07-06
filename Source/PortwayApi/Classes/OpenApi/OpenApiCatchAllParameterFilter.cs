using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Serilog;

namespace PortwayApi.Classes;

/// <summary>Custom document transformer to properly handle catchall parameters</summary>
public class OpenApiCatchAllParameterFilter : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        // Find and remove any path with {**catchall} in it, as these are causing conflicts
        var pathsToRemove = document.Paths.Keys
            .Where(p => p.Contains("{**catchall}"))
            .ToList();

        foreach (var path in pathsToRemove)
        {
            Log.Debug($"Removing catchall path from OpenAPI docs: {path}");
            document.Paths.Remove(path);
        }

        return Task.CompletedTask;
    }
}
