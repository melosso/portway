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

        var endpoints = OpenApiEndpointCatalog.All().ToList();

        foreach (var (pathKey, pathItem) in document.Paths)
        {
            if (OpenApiEndpointCatalog.Owner(endpoints, pathKey)?.Version is not { } version || EndpointVersion.IsDefault(version))
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

    /// <summary>
    /// Removes the summary suffix and version badge from a single-version document; the operation id keeps its suffix
    /// </summary>
    internal static void RemoveVersionLabels(OpenApiOperation operation, string version)
    {
        if (EndpointVersion.IsDefault(version))
        {
            return;
        }

        var suffix = $"({version})";
        if (operation.Summary?.EndsWith(suffix, StringComparison.Ordinal) == true)
        {
            var summary = operation.Summary[..^suffix.Length].TrimEnd();
            operation.Summary = summary.Length == 0 ? null : summary;
        }

        if (operation.Extensions?.TryGetValue("x-badges", out var existing) == true
            && existing is JsonNodeExtension { Node: JsonArray list }
            && list.Count > 0
            && list[0]?["name"]?.GetValue<string>() == version)
        {
            list.RemoveAt(0);
            if (list.Count == 0)
            {
                operation.Extensions.Remove("x-badges");
            }
        }
    }
}
