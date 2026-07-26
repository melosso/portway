namespace PortwayApi.Classes.OpenApi;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using PortwayApi.Classes;

/// <summary>Shows endpoints switched off through Enabled as unavailable, so an outage is visible rather than looking like a deletion</summary>
public class DisabledEndpointDocumentFilter : IOpenApiDocumentTransformer
{
    private const string SummaryPrefix = "[Disabled] ";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var disabledBases = OpenApiEndpointCatalog.All()
            .Where(e => !e.Definition.Enabled)
            .Select(e => e.BasePath)
            .ToList();

        if (disabledBases.Count == 0 || document.Paths is null)
        {
            return Task.CompletedTask;
        }

        foreach (var (pathKey, pathItem) in document.Paths)
        {
            if (pathItem.Operations is null)
            {
                continue;
            }

            if (!disabledBases.Any(b => OpenApiEndpointCatalog.Covers(b, pathKey)))
            {
                continue;
            }

            foreach (var operation in pathItem.Operations.Values)
            {
                // Deprecated is the only state the format itself can express, so it doubles as the visual cue
                operation.Deprecated = true;

                if (operation.Summary?.StartsWith(SummaryPrefix, StringComparison.Ordinal) != true)
                {
                    operation.Summary = SummaryPrefix + operation.Summary;
                }
            }
        }

        return Task.CompletedTask;
    }
}
