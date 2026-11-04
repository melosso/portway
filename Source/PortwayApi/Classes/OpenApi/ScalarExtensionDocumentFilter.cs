namespace PortwayApi.Classes.OpenApi;

using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

/// <summary>
/// Labels operations with x-badges and x-scalar-stability; ShowBadges controls the MCP and OData badges, configured badges always show
/// </summary>
public sealed class ScalarExtensionDocumentFilter(IOptionsMonitor<OpenApiSettings> settings) : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        if (document.Paths is null)
        {
            return Task.CompletedTask;
        }

        var showAutomatic = settings.CurrentValue.ShowBadges;
        var endpoints = OpenApiEndpointCatalog.All().ToList();

        foreach (var (pathKey, pathItem) in document.Paths)
        {
            var definition = OpenApiEndpointCatalog.Owner(endpoints, pathKey);

            foreach (var operation in pathItem.Operations?.Values ?? Enumerable.Empty<OpenApiOperation>())
            {
                var badges = new JsonArray();
                if (showAutomatic && definition?.Mcp?.Exposed == true)
                {
                    badges.Add(new JsonObject { ["name"] = "MCP" });
                }

                if (showAutomatic && operation.Parameters?.Any(p => p.Name == "$filter") == true)
                {
                    badges.Add(new JsonObject { ["name"] = "OData" });
                }

                foreach (var badge in definition?.Badges ?? [])
                {
                    badges.Add(ToNode(badge));
                }

                if (badges.Count > 0)
                {
                    SetExtension(operation, "x-badges", badges);
                }

                if (definition?.Stability is { } stability && !operation.Deprecated)
                {
                    SetExtension(operation, "x-scalar-stability", JsonValue.Create(stability.ToLowerInvariant()));
                }
            }
        }

        return Task.CompletedTask;
    }

    private static JsonObject ToNode(EndpointBadge badge)
    {
        var node = new JsonObject { ["name"] = badge.Name };
        if (badge.Position is { } position)
        {
            node["position"] = position.ToLowerInvariant();
        }

        if (badge.Color is { } color)
        {
            node["color"] = color;
        }

        return node;
    }

    private static void SetExtension(OpenApiOperation operation, string name, JsonNode value)
    {
        operation.Extensions ??= new Dictionary<string, IOpenApiExtension>();
        operation.Extensions[name] = new JsonNodeExtension(value);
    }
}
