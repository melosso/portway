namespace PortwayApi.Classes.OpenApi;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

/// <summary>Standardizes every response onto the shared per-status-code phrase (summary) and explanation (description); endpoint specifics stay on the operation</summary>
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
                    if (!int.TryParse(statusCode, out var code) || response is not OpenApiResponse concrete)
                    {
                        continue;
                    }

                    if (StandardResponses.SummaryFor(code) is { } summary)
                    {
                        concrete.Summary = summary;
                    }

                    if (StandardResponses.DescriptionFor(code) is { } description)
                    {
                        concrete.Description = description;
                    }
                }
            }
        }

        return Task.CompletedTask;
    }
}
