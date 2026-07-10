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
        // Base path template -> definition, only for endpoints that supplied examples (SQL + Proxy carry Documentation)
        var byBase = new Dictionary<string, EndpointDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in EndpointHandler.GetSqlEndpoints().Concat(EndpointHandler.GetProxyEndpoints()))
        {
            if (kv.Value.Documentation?.Examples is { Count: > 0 })
            {
                byBase[$"/api/{{env}}/{kv.Value.FullPath}"] = kv.Value;
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

            var definition = byBase.FirstOrDefault(b =>
                pathKey.Equals(b.Key, StringComparison.OrdinalIgnoreCase) ||
                pathKey.StartsWith(b.Key + "(", StringComparison.OrdinalIgnoreCase) ||
                pathKey.StartsWith(b.Key + "/", StringComparison.OrdinalIgnoreCase)).Value;

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

                // Apply to the first success (2xx) response's JSON body
                var successResponse = operation.Responses?
                    .FirstOrDefault(r => r.Key.StartsWith("2", StringComparison.Ordinal)).Value;

                if (successResponse?.Content is not null &&
                    successResponse.Content.TryGetValue("application/json", out var media) &&
                    media is OpenApiMediaType concrete)
                {
                    // DeepClone so the same node is not parented into more than one place
                    concrete.Example = example.DeepClone();
                }
            }
        }

        return Task.CompletedTask;
    }
}
