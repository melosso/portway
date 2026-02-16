using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace PortwayApi.Classes.Swagger;

/// <summary>
/// Document filter that injects dynamic configuration values into the OpenAPI document
/// </summary>
public class DynamicSwaggerDocumentFilter : IDocumentFilter
{
    private readonly IOptionsMonitor<SwaggerSettings> _swaggerMonitor;

    public DynamicSwaggerDocumentFilter(IOptionsMonitor<SwaggerSettings> swaggerMonitor)
    {
        _swaggerMonitor = swaggerMonitor;
    }

    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        // Read current configuration values on each request
        var settings = _swaggerMonitor.CurrentValue;

        // Update OpenAPI document metadata with current values
        swaggerDoc.Info.Title = settings.Title;
        swaggerDoc.Info.Version = settings.Version;
        swaggerDoc.Info.Description = settings.Description;

        if (settings.Contact != null)
        {
            swaggerDoc.Info.Contact = new OpenApiContact
            {
                Name = settings.Contact.Name,
                Email = settings.Contact.Email
            };
        }
    }
}
