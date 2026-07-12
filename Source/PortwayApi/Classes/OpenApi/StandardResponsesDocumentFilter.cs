namespace PortwayApi.Classes;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

/// <summary>Registers the shared error-response component schemas so operations can reference them</summary>
public class StandardResponsesDocumentFilter : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        StandardResponses.EnsureSchemas(document);
        return Task.CompletedTask;
    }
}
