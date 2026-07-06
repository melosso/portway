using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Serilog;

namespace PortwayApi.Classes;

/// <summary>Custom operation transformer to add security requirements to all operations</summary>
public class OpenApiOperationFilter : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        // Skip if this is an OpenAPI docs operation
        if (context.Description.RelativePath?.StartsWith("openapi-docs") == true)
        {
            return Task.CompletedTask;
        }

        // Add security requirements to all operations
        operation.Security ??= new List<OpenApiSecurityRequirement>();
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecuritySchemeReference("Bearer"),
                new List<string>()
            }
        });

        // Add standard response codes
        operation.Responses ??= new OpenApiResponses();

        if (!operation.Responses.ContainsKey("401"))
            operation.Responses.Add("401", new OpenApiResponse { Description = "Unauthorized" });

        if (!operation.Responses.ContainsKey("403"))
            operation.Responses.Add("403", new OpenApiResponse { Description = "Forbidden" });

        if (!operation.Responses.ContainsKey("500"))
            operation.Responses.Add("500", new OpenApiResponse { Description = "Server Error" });

        return Task.CompletedTask;
    }
}
