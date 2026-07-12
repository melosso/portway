namespace PortwayApi.Classes.OpenApi;

using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using PortwayApi.Classes;

/// <summary>Applies each namespace's optional NamespaceIcon as a Scalar x-scalar-icon extension on its tag</summary>
public class TagIconDocumentFilter : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        if (document.Tags is null || document.Tags.Count == 0)
        {
            return Task.CompletedTask;
        }

        // Tag name -> icon; the first endpoint that declares an icon for a namespace tag wins
        var icons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in AllDefinitions())
        {
            if (!string.IsNullOrWhiteSpace(definition.NamespaceIcon))
            {
                icons.TryAdd(definition.DocumentationTag, definition.NamespaceIcon!);
            }
        }

        if (icons.Count == 0)
        {
            return Task.CompletedTask;
        }

        foreach (var tag in document.Tags)
        {
            if (tag.Name is not null && icons.TryGetValue(tag.Name, out var icon))
            {
                tag.Extensions ??= new Dictionary<string, IOpenApiExtension>();
                tag.Extensions["x-scalar-icon"] = new JsonNodeExtension(JsonValue.Create(icon));
            }
        }

        return Task.CompletedTask;
    }

    private static IEnumerable<EndpointDefinition> AllDefinitions() =>
        EndpointHandler.GetSqlEndpoints().Values
            .Concat(EndpointHandler.GetProxyEndpoints().Values)
            .Concat(EndpointHandler.GetFileEndpoints().Values)
            .Concat(EndpointHandler.GetStaticEndpoints().Values)
            .Concat(EndpointHandler.GetSqlWebhookEndpoints().Values);
}
