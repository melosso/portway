namespace PortwayApi.Classes.OpenApi;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using PortwayApi.Classes;

/// <summary>Marks operations of endpoints in a given config state; Deprecated is the only state OpenAPI can express</summary>
public sealed class EndpointStateDocumentFilter : IOpenApiDocumentTransformer
{
    private readonly Func<EndpointDefinition, bool> _matches;
    private readonly string? _summaryPrefix;

    public EndpointStateDocumentFilter(Func<EndpointDefinition, bool> matches, string? summaryPrefix = null)
    {
        _matches = matches;
        _summaryPrefix = summaryPrefix;
    }

    /// <summary>Endpoints switched off through Enabled, so an outage does not read as a deletion</summary>
    public static EndpointStateDocumentFilter Disabled() => new(d => !d.Enabled, "[Disabled] ");

    /// <summary>Endpoints flagged Deprecated in config</summary>
    public static EndpointStateDocumentFilter Deprecated() => new(d => d.Deprecated);

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var matchedBases = OpenApiEndpointCatalog.All()
            .Where(e => _matches(e.Definition))
            .Select(e => e.BasePath)
            .ToList();

        if (matchedBases.Count == 0 || document.Paths is null)
        {
            return Task.CompletedTask;
        }

        foreach (var (pathKey, pathItem) in document.Paths)
        {
            if (pathItem.Operations is null)
            {
                continue;
            }

            if (!matchedBases.Any(b => OpenApiEndpointCatalog.Covers(b, pathKey)))
            {
                continue;
            }

            foreach (var operation in pathItem.Operations.Values)
            {
                operation.Deprecated = true;

                if (_summaryPrefix is not null &&
                    operation.Summary?.StartsWith(_summaryPrefix, StringComparison.Ordinal) != true)
                {
                    operation.Summary = _summaryPrefix + operation.Summary;
                }
            }
        }

        return Task.CompletedTask;
    }
}
