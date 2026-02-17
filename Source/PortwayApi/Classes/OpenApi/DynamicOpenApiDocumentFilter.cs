using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace PortwayApi.Classes.OpenApi;

/// <summary>
/// Document transformer that injects dynamic configuration values into the OpenAPI document
/// </summary>
public class DynamicOpenApiDocumentFilter : IOpenApiDocumentTransformer
{
    private readonly IOptionsMonitor<OpenApiSettings> _openApiMonitor;

    public DynamicOpenApiDocumentFilter(IOptionsMonitor<OpenApiSettings> openApiMonitor)
    {
        _openApiMonitor = openApiMonitor;
    }

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        // Read current configuration values on each request
        var settings = _openApiMonitor.CurrentValue;

        // Update OpenAPI document metadata with current values
        document.Info.Title = settings.Title;
        document.Info.Version = settings.Version;
        document.Info.Description = settings.Description;

        if (settings.Contact != null)
        {
            document.Info.Contact = new OpenApiContact
            {
                Name = settings.Contact.Name,
                Email = settings.Contact.Email
            };
        }

        return Task.CompletedTask;
    }
}
