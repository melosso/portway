using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Serilog;

namespace PortwayApi.Classes;

/// <summary>Document transformer that ensures endpoints are sorted alphabetically by path rather than grouped by tag</summary>
public class AlphabeticalEndpointSorter : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var paths = document.Paths.OrderBy(x => x.Key).ToDictionary(x => x.Key, x => x.Value);

        document.Paths.Clear();
        foreach (var path in paths)
        {
            document.Paths.Add(path.Key, path.Value);
        }

        return Task.CompletedTask;
    }
}
