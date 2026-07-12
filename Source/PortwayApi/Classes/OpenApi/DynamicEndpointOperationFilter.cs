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

        // Response codes are documented consistently by the endpoint document filters (see StandardResponses)

        return Task.CompletedTask;
    }
}
