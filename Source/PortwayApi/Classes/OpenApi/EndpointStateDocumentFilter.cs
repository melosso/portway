namespace PortwayApi.Classes.OpenApi;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using PortwayApi.Classes;
using PortwayApi.Helpers;

/// <summary>
/// Marks operations of endpoints in a given config state; Deprecated is the only state OpenAPI can express
/// </summary>
public sealed class EndpointStateDocumentFilter : IOpenApiDocumentTransformer
{
    private readonly Func<EndpointDefinition, bool> _matches;
    private readonly string? _summaryPrefix;
    private readonly bool _documentLifecycleHeaders;

    public EndpointStateDocumentFilter(Func<EndpointDefinition, bool> matches, string? summaryPrefix = null, bool documentLifecycleHeaders = false)
    {
        _matches = matches;
        _summaryPrefix = summaryPrefix;
        _documentLifecycleHeaders = documentLifecycleHeaders;
    }

    /// <summary>
    /// Endpoints switched off through Enabled, so an outage does not read as a deletion
    /// </summary>
    public static EndpointStateDocumentFilter Disabled() => new(d => !d.Enabled, "[Disabled] ");

    /// <summary>
    /// Endpoints flagged Deprecated in config
    /// </summary>
    public static EndpointStateDocumentFilter Deprecated() => new(d => d.Deprecated, documentLifecycleHeaders: true);

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var matched = OpenApiEndpointCatalog.All()
            .Where(e => _matches(e.Definition))
            .ToList();

        if (matched.Count == 0 || document.Paths is null)
        {
            return Task.CompletedTask;
        }

        foreach (var (pathKey, pathItem) in document.Paths)
        {
            if (pathItem.Operations is null)
            {
                continue;
            }

            if (matched.FirstOrDefault(e => OpenApiEndpointCatalog.Covers(e.BasePath, pathKey)).Definition is not { } definition)
            {
                continue;
            }

            var lifecycleHeaders = _documentLifecycleHeaders ? LifecycleHeaders(definition) : [];

            foreach (var operation in pathItem.Operations.Values)
            {
                operation.Deprecated = true;

                if (_summaryPrefix is not null &&
                    operation.Summary?.StartsWith(_summaryPrefix, StringComparison.Ordinal) != true)
                {
                    operation.Summary = _summaryPrefix + operation.Summary;
                }

                foreach (var (code, response) in operation.Responses ?? [])
                {
                    if (!code.StartsWith('2') || response is not OpenApiResponse concrete)
                    {
                        continue;
                    }

                    concrete.Headers ??= new Dictionary<string, IOpenApiHeader>();
                    foreach (var (name, header) in lifecycleHeaders)
                    {
                        concrete.Headers[name] = header;
                    }
                }
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// The headers EndpointLifecycleHeaders sends for this endpoint
    /// </summary>
    private static List<(string Name, OpenApiHeader Header)> LifecycleHeaders(EndpointDefinition definition)
    {
        var headers = new List<(string, OpenApiHeader)>();
        if (definition.DeprecatedSince is not null)
        {
            headers.Add(("Deprecation", Header("When the endpoint was deprecated, as @unix-seconds (RFC 9745)")));
        }

        if (definition.Sunset is not null)
        {
            headers.Add(("Sunset", Header("When the endpoint stops responding, as an HTTP date (RFC 8594)")));
        }

        if (EndpointLifecycleHeaders.Successor(definition) is not null)
        {
            headers.Add(("Link", Header("The successor version, with rel=\"successor-version\"")));
        }

        return headers;
    }

    private static OpenApiHeader Header(string description) => new()
    {
        Description = description,
        Schema = new OpenApiSchema { Type = JsonSchemaType.String }
    };
}
