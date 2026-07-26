namespace PortwayApi.Classes.OpenApi;

using System.Linq;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using PortwayApi.Classes;

/// <summary>Uses author-provided examples from each endpoint's Documentation block as the OpenAPI success-response example</summary>
public class ConfigExampleDocumentFilter : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        // Only endpoints that supplied examples
        var byBase = new Dictionary<string, EndpointDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var (basePath, definition) in OpenApiEndpointCatalog.All())
        {
            if (definition.Documentation?.Examples is { Count: > 0 })
            {
                byBase[basePath] = definition;
            }
        }

        if (byBase.Count == 0 || document.Paths is null)
        {
            return Task.CompletedTask;
        }

        foreach (var (pathKey, pathItem) in document.Paths)
        {
            if (pathItem.Operations is null)
            {
                continue;
            }

            var definition = byBase.FirstOrDefault(b => OpenApiEndpointCatalog.Covers(b.Key, pathKey)).Value;

            var examples = definition?.Documentation?.Examples;
            if (examples is null)
            {
                continue;
            }

            foreach (var (httpMethod, operation) in pathItem.Operations)
            {
                if (!examples.TryGetValue(httpMethod.Method, out var example) || example is null)
                {
                    continue;
                }

                // Apply to the first success (2xx) response body
                var successResponse = operation.Responses?
                    .FirstOrDefault(r => r.Key.StartsWith("2", StringComparison.Ordinal)).Value;

                if (successResponse?.Content is null || successResponse.Content.Count == 0)
                {
                    continue;
                }

                // Prefer JSON; static endpoints declare a single non-JSON media type instead
                if (!successResponse.Content.TryGetValue("application/json", out var media))
                {
                    media = successResponse.Content.Values.First();
                }

                if (media is OpenApiMediaType concrete)
                {
                    // DeepClone so the same node is not parented into more than one place
                    concrete.Example = example.DeepClone();
                }
            }
        }

        return Task.CompletedTask;
    }
}
