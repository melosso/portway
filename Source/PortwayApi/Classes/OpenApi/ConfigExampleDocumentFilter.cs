namespace PortwayApi.Classes.OpenApi;

using System.Linq;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using PortwayApi.Classes;

/// <summary>
/// Uses author-provided examples from each endpoint's Documentation block as the OpenAPI success-response example
/// </summary>
public sealed class ConfigExampleDocumentFilter : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var endpoints = OpenApiEndpointCatalog.All().ToList();

        if (!endpoints.Any(e => e.Definition.Documentation?.Examples is { Count: > 0 }) || document.Paths is null)
        {
            return Task.CompletedTask;
        }

        foreach (var (pathKey, pathItem) in document.Paths)
        {
            if (pathItem.Operations is null)
            {
                continue;
            }

            var examples = OpenApiEndpointCatalog.Owner(endpoints, pathKey)?.Documentation?.Examples;
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
