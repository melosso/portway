using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace PortwayApi.Classes;

public class DynamicEndpointOperationFilter : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.Description.RelativePath == null ||
            context.Description.RelativePath.StartsWith("openapi-docs", StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        // Initialize security collection if null
        operation.Security ??= new List<OpenApiSecurityRequirement>();

        // Add security requirement
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecuritySchemeReference("Bearer"),
                new List<string>()
            }
        });

        // Initialize responses if null
        operation.Responses ??= new OpenApiResponses();

        // Add standard response codes
        if (!operation.Responses.ContainsKey("401"))
            operation.Responses.Add("401", new OpenApiResponse { Description = "Unauthorized" });
        if (!operation.Responses.ContainsKey("403"))
            operation.Responses.Add("403", new OpenApiResponse { Description = "Forbidden" });
        if (!operation.Responses.ContainsKey("404"))
            operation.Responses.Add("404", new OpenApiResponse { Description = "Not Found" });
        if (!operation.Responses.ContainsKey("500"))
            operation.Responses.Add("500", new OpenApiResponse { Description = "Server Error" });

        return Task.CompletedTask;
    }
}
