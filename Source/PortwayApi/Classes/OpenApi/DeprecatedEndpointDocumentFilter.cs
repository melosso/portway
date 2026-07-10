namespace PortwayApi.Classes.OpenApi;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using PortwayApi.Classes;

/// <summary>Marks all operations of endpoints flagged Deprecated in config as deprecated in the OpenAPI document</summary>
public class DeprecatedEndpointDocumentFilter : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        // Base path templates of endpoints that opted into deprecation (SQL + Proxy carry the flag)
        var deprecatedBases = EndpointHandler.GetSqlEndpoints()
            .Concat(EndpointHandler.GetProxyEndpoints())
            .Where(kv => kv.Value.Deprecated)
            .Select(kv => $"/api/{{env}}/{kv.Value.FullPath}")
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

            // Match the base path itself and its id / sub-path variants, without prefix-matching a longer sibling name
            var isDeprecated = deprecatedBases.Any(b =>
                pathKey.Equals(b, StringComparison.OrdinalIgnoreCase) ||
                pathKey.StartsWith(b + "(", StringComparison.OrdinalIgnoreCase) ||
                pathKey.StartsWith(b + "/", StringComparison.OrdinalIgnoreCase));

            if (!isDeprecated)
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
