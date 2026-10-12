namespace PortwayApi.Classes.OpenApi;

using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

/// <summary>
/// Labels operations with x-badges for what the endpoint offers: MCP when exposed as a tool, OData when the operation takes $filter
/// </summary>
public sealed class EndpointBadgeDocumentFilter(IOptionsMonitor<OpenApiSettings> settings) : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        if (!settings.CurrentValue.ShowBadges || document.Paths is null)
        {
            return Task.CompletedTask;
        }

        var endpoints = OpenApiEndpointCatalog.All().ToList();

        foreach (var (pathKey, pathItem) in document.Paths)
        {
            // the longest base wins so a parent route never labels a nested endpoint
            var mcp = endpoints
                .Where(e => OpenApiEndpointCatalog.Covers(e.BasePath, pathKey))
                .MaxBy(e => e.BasePath.Length)
                .Definition?.Mcp?.Exposed == true;

            foreach (var operation in pathItem.Operations?.Values ?? Enumerable.Empty<OpenApiOperation>())
            {
                var badges = new JsonArray();
                if (mcp)
                {
                    badges.Add(new JsonObject { ["name"] = "MCP" });
                }

                if (operation.Parameters?.Any(p => p.Name == "$filter") == true)
                {
                    badges.Add(new JsonObject { ["name"] = "OData" });
                }

                if (badges.Count > 0)
                {
                    operation.Extensions ??= new Dictionary<string, IOpenApiExtension>();
                    operation.Extensions["x-badges"] = new JsonNodeExtension(badges);
                }
            }
        }

        return Task.CompletedTask;
    }
}
