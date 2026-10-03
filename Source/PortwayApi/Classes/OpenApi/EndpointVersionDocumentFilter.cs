namespace PortwayApi.Classes.OpenApi;

using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

/// <summary>
/// Suffixes operation ids and summaries of versioned endpoints with their version and adds a version badge
/// </summary>
public sealed class EndpointVersionDocumentFilter : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        if (document.Paths is null)
        {
            return Task.CompletedTask;
        }

        var versioned = OpenApiEndpointCatalog.All()
            .Select(e => (e.BasePath, Version: e.Definition.Version))
            .Where(e => !EndpointVersion.IsDefault(e.Version))
            .ToList();

        foreach (var (pathKey, pathItem) in document.Paths)
        {
            if (versioned.FirstOrDefault(e => OpenApiEndpointCatalog.Covers(e.BasePath, pathKey)).Version is not { } version)
            {
                continue;
            }

            foreach (var operation in pathItem.Operations?.Values ?? Enumerable.Empty<OpenApiOperation>())
            {
                operation.OperationId = operation.OperationId is null ? null : $"{operation.OperationId.Replace("@" + version, "")}_{version}";
                operation.Summary = $"{operation.Summary} ({version})".TrimStart();

                var badges = operation.Extensions?.TryGetValue("x-badges", out var existing) == true && existing is JsonNodeExtension { Node: JsonArray list }
                    ? list
                    : new JsonArray();
                badges.Insert(0, new JsonObject { ["name"] = version });
                operation.Extensions ??= new Dictionary<string, IOpenApiExtension>();
                operation.Extensions["x-badges"] = new JsonNodeExtension(badges);
            }
        }

        return Task.CompletedTask;
    }
}
