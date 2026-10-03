namespace PortwayApi.Classes.OpenApi;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

/// <summary>
/// Titles every group tag no endpoint labelled with its last namespace segment, after all labels are declared so load order cannot pick the title
/// </summary>
public sealed class GroupTitleDocumentFilter : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        foreach (var tag in document.Tags ?? Enumerable.Empty<OpenApiTag>())
        {
            if (OpenApiTags.GroupPath(tag.Name) is { } path)
            {
                tag.Summary ??= path[(path.LastIndexOf('/') + 1)..];
            }
        }

        return Task.CompletedTask;
    }
}
