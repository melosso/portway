using System;
using System.Collections.Generic;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace PortwayApi.Classes;

public class DynamicEndpointOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.ApiDescription.RelativePath == null || 
            context.ApiDescription.RelativePath.StartsWith("swagger", StringComparison.OrdinalIgnoreCase))
        {
            return; 
        }

        // Initialize security collection if null
        operation.Security ??= new List<OpenApiSecurityRequirement>();

        // Add security requirement
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                },
                new string[] { }
            }
        });
        
        // Initialize responses if null
        operation.Responses ??= new OpenApiResponses();
        
        // Add standard response codes
        operation.Responses.Add("401", new OpenApiResponse { Description = "Unauthorized" });
        operation.Responses.Add("403", new OpenApiResponse { Description = "Forbidden" });
        operation.Responses.Add("404", new OpenApiResponse { Description = "Not Found" });
        operation.Responses.Add("500", new OpenApiResponse { Description = "Server Error" });
    }
}