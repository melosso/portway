namespace PortwayApi.Classes.OpenApi;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using PortwayApi.Classes;

/// <summary>Marks all operations of endpoints flagged Deprecated in config as deprecated in the OpenAPI document</summary>
public class DeprecatedEndpointDocumentFilter : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var deprecatedBases = OpenApiEndpointCatalog.All()
            .Where(e => e.Definition.Deprecated)
            .Select(e => e.BasePath)
            .ToList();

        if (deprecatedBases.Count == 0 || document.Paths is null)
        {
            return Task.CompletedTask;
        }

        foreach (var (pathKey, pathItem) in document.Paths)
        {
            if (pathItem.Operations is null)
            {
                continue;
            }

            if (!deprecatedBases.Any(b => OpenApiEndpointCatalog.Covers(b, pathKey)))
            {
                continue;
            }

            foreach (var operation in pathItem.Operations.Values)
            {
                operation.Deprecated = true;
            }
        }

        return Task.CompletedTask;
    }
}
