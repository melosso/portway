namespace PortwayApi.Classes;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

/// <summary>Standardizes every response description to the shared per-status-code phrase; endpoint specifics stay on the operation summary/description</summary>
public class ResponseDescriptionDocumentFilter : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        if (document.Paths is null)
        {
            return Task.CompletedTask;
        }

        foreach (var (_, pathItem) in document.Paths)
        {
            if (pathItem.Operations is null)
            {
                continue;
            }

            foreach (var (_, operation) in pathItem.Operations)
            {
                if (operation.Responses is null)
                {
                    continue;
                }

                foreach (var (statusCode, response) in operation.Responses)
                {
                    if (int.TryParse(statusCode, out var code) &&
                        StandardResponses.DescriptionFor(code) is { } standard &&
                        response is OpenApiResponse concrete)
                    {
                        concrete.Description = standard;
                    }
                }
            }
        }

        return Task.CompletedTask;
    }
}
