using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace PortwayApi.Classes;

public class CompositeEndpointDocumentFilter : IDocumentFilter
{
    private readonly ILogger<CompositeEndpointDocumentFilter> _logger;

    public CompositeEndpointDocumentFilter(ILogger<CompositeEndpointDocumentFilter> logger)
    {
        _logger = logger;
    }

    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        try
        {
            // Get proxy endpoints directly to access full namespace information
            var proxyEndpoints = EndpointHandler.GetProxyEndpoints();
            
            // Filter for composite endpoints only
            var compositeEndpoints = proxyEndpoints
                .Where(kvp => kvp.Value.IsComposite)
                .ToList();
            
            // Get allowed environments for parameter description
            var allowedEnvironments = GetAllowedEnvironments();
            
            // Collect tags with descriptions for proper namespace grouping
            var documentTags = new Dictionary<string, string>();

            // Sort composite endpoints by name to ensure alphabetical order in documentation
            var sortedCompositeEndpoints = compositeEndpoints
                .OrderBy(ep => ep.Value.SwaggerTag, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Create paths for each composite endpoint
            foreach (var endpoint in sortedCompositeEndpoints)
            {
                string endpointKey = endpoint.Key;
                var definition = endpoint.Value;
                
                // Collect tag description if provided using the SwaggerTag for proper namespace grouping
                if (!string.IsNullOrWhiteSpace(definition.Documentation?.TagDescription) && 
                    !documentTags.ContainsKey(definition.SwaggerTag))
                {
                    documentTags[definition.SwaggerTag] = definition.Documentation.TagDescription;
                }
                
                // Use the namespaced path from FullPath instead of hardcoded /composite/
                string path = $"/api/{{env}}/{definition.FullPath}";
                
                // If the path doesn't exist yet, create it
                if (!swaggerDoc.Paths.ContainsKey(path))
                {
                    swaggerDoc.Paths.Add(path, new OpenApiPathItem());
                }

                // Create the POST operation for the composite endpoint
                var operation = new OpenApiOperation
                {
                    Tags = new List<OpenApiTag> { new OpenApiTag { Name = definition.SwaggerTag } },
                    Summary = definition.Documentation?.MethodDescriptions?.GetValueOrDefault("POST") ?? $"Execute {definition.EndpointName} composite endpoint",
                    Description = definition.Documentation?.MethodDocumentation?.GetValueOrDefault("POST") ?? definition.CompositeConfig?.Description ?? $"Executes the {definition.EndpointName} composite process with multiple steps",
                    OperationId = $"composite_{definition.FullPath}".Replace(" ", "_").Replace("/", "_"),
                    Parameters = new List<OpenApiParameter>()
                };

                // Add environment parameter
                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = "env",
                    In = ParameterLocation.Path,
                    Required = true,
                    Schema = new OpenApiSchema { Type = "string", Enum = allowedEnvironments.Select(e => new OpenApiString(e)).Cast<IOpenApiAny>().ToList() },
                    Description = $"Environment to target. Allowed values: {string.Join(", ", allowedEnvironments)}"
                });

                // Create request body schema
                var requestSchema = new OpenApiSchema
                {
                    Type = "object",
                    Properties = new Dictionary<string, OpenApiSchema>()
                };

                // Add request body
                operation.RequestBody = new OpenApiRequestBody
                {
                    Required = true,
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new OpenApiMediaType
                        {
                            Schema = requestSchema
                        }
                    },
                    Description = "Composite request data"
                };

                // Check if this is the SalesOrder endpoint to add specialized examples
                if (string.Equals(definition.EndpointName, "SalesOrder", StringComparison.OrdinalIgnoreCase))
                {
                    // Add SalesOrder-specific example
                    AddSalesOrderExample(operation, requestSchema);
                }
                else if (definition.CompositeConfig?.Steps != null)
                {
                    // Add generic examples based on steps for other composite endpoints
                    foreach (var step in definition.CompositeConfig.Steps)
                    {
                        if (step.IsArray && !string.IsNullOrEmpty(step.ArrayProperty))
                        {
                            requestSchema.Properties[step.ArrayProperty] = new OpenApiSchema
                            {
                                Type = "array",
                                Items = new OpenApiSchema { Type = "object" },
                                Description = $"Array of items for {step.Name} step"
                            };
                        }
                        
                        // Add any commonly needed properties here in a generic way
                        if (step.SourceProperty != null && !requestSchema.Properties.ContainsKey(step.SourceProperty))
                        {
                            requestSchema.Properties[step.SourceProperty] = new OpenApiSchema
                            {
                                Type = "object",
                                Description = $"Data for {step.Name} step"
                            };
                        }
                    }
                }

                // Add response
                operation.Responses = new OpenApiResponses
                {
                    ["200"] = new OpenApiResponse
                    {
                        Description = "Successful response",
                        Content = new Dictionary<string, OpenApiMediaType>
                        {
                            ["application/json"] = new OpenApiMediaType
                            {
                                Schema = new OpenApiSchema
                                {
                                    Type = "object",
                                    Properties = new Dictionary<string, OpenApiSchema>
                                    {
                                        { "Success", new OpenApiSchema { Type = "boolean" } },
                                        { "StepResults", new OpenApiSchema { Type = "object" } }
                                    }
                                }
                            }
                        }
                    },
                    ["400"] = new OpenApiResponse { Description = "Bad request or execution error" },
                    ["401"] = new OpenApiResponse { Description = "Unauthorized" },
                    ["404"] = new OpenApiResponse { Description = "Composite endpoint not found" }
                };

                // Add security requirement
                operation.Security = new List<OpenApiSecurityRequirement>
                {
                    new OpenApiSecurityRequirement
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
                    }
                };

                // Add the operation to the path
                swaggerDoc.Paths[path].Operations[OperationType.Post] = operation;
            }
            
            // Add collected tags to the document
            if (documentTags.Any())
            {
                // Initialize tags collection if it doesn't exist
                swaggerDoc.Tags ??= new List<OpenApiTag>();
                
                foreach (var tag in documentTags)
                {
                    // Add tag if it doesn't already exist
                    if (!swaggerDoc.Tags.Any(t => t.Name.Equals(tag.Key, StringComparison.OrdinalIgnoreCase)))
                    {
                        swaggerDoc.Tags.Add(new OpenApiTag
                        {
                            Name = tag.Key,
                            Description = tag.Value
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating Swagger documentation for composite endpoints");
        }
    }

    /// <summary>
    /// Add specialized example for SalesOrder composite endpoint
    /// </summary>
    private void AddSalesOrderExample(OpenApiOperation operation, OpenApiSchema requestSchema)
    {
        // Add Header property
        requestSchema.Properties["Header"] = new OpenApiSchema
        {
            Type = "object",
            Properties = new Dictionary<string, OpenApiSchema>
            {
                { "OrderDebtor", new OpenApiSchema { Type = "string", Example = new OpenApiString("60093") } },
                { "YourReference", new OpenApiSchema { Type = "string", Example = new OpenApiString("Connect async") } }
            },
            Description = "Header information for the sales order"
        };
        
        // Add Lines property with item examples
        var linesSchema = new OpenApiSchema
        {
            Type = "array",
            Description = "Array of order lines",
            Items = new OpenApiSchema
            {
                Type = "object",
                Properties = new Dictionary<string, OpenApiSchema>
                {
                    { "Itemcode", new OpenApiSchema { Type = "string", Example = new OpenApiString("ITEM-001") } },
                    { "Quantity", new OpenApiSchema { Type = "number", Format = "float", Example = new OpenApiFloat(2.0f) } },
                    { "Price", new OpenApiSchema { Type = "number", Format = "float", Example = new OpenApiFloat(0.0f) } }
                }
            }
        };
        
        requestSchema.Properties["Lines"] = linesSchema;
        
        // Add an example for the entire request
        var example = new OpenApiObject
        {
            ["Header"] = new OpenApiObject
            {
                ["OrderDebtor"] = new OpenApiString("60093"),
                ["YourReference"] = new OpenApiString("Connect async")
            },
            ["Lines"] = new OpenApiArray
            {
                new OpenApiObject
                {
                    ["Itemcode"] = new OpenApiString("ITEM-001"),
                    ["Quantity"] = new OpenApiInteger(2),
                    ["Price"] = new OpenApiFloat(0.0f)
                },
                new OpenApiObject
                {
                    ["Itemcode"] = new OpenApiString("ITEM-002"),
                    ["Quantity"] = new OpenApiInteger(4),
                    ["Price"] = new OpenApiFloat(0.0f)
                },
                new OpenApiObject
                {
                    ["Itemcode"] = new OpenApiString("BEK0003"),
                    ["Quantity"] = new OpenApiInteger(5),
                    ["Price"] = new OpenApiFloat(0.0f)
                }
            }
        };
        
        // Add the example to the media type
        if (!operation.RequestBody.Content.ContainsKey("application/json"))
        {
            operation.RequestBody.Content["application/json"] = new OpenApiMediaType();
        }
        
        operation.RequestBody.Content["application/json"].Schema = requestSchema;
        operation.RequestBody.Content["application/json"].Examples = new Dictionary<string, OpenApiExample>
        {
            ["default"] = new OpenApiExample
            {
                Value = example,
                Summary = "Sample sales order with multiple lines"
            }
        };
    }

    private List<string> GetAllowedEnvironments()
    {
        try
        {
            var settingsFile = Path.Combine(Directory.GetCurrentDirectory(), "environments", "settings.json");
            if (File.Exists(settingsFile))
            {
                var settingsJson = File.ReadAllText(settingsFile);
                
                // Match the structure used in EnvironmentSettings class
                var settings = JsonSerializer.Deserialize<SettingsModel>(settingsJson);
                if (settings?.Environment?.AllowedEnvironments != null && 
                    settings.Environment.AllowedEnvironments.Any())
                {
                    return settings.Environment.AllowedEnvironments;
                }
            }
            
            // Return default if settings not found
            return new List<string> { "prod", "dev" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading environment settings");
            return new List<string> { "prod", "dev" };
        }
    }

    // Match the classes used in EnvironmentSettings
    private class SettingsModel
    {
        public EnvironmentModel Environment { get; set; } = new EnvironmentModel();
    }

    private class EnvironmentModel
    {
        public string ServerName { get; set; } = ".";
        public List<string> AllowedEnvironments { get; set; } = new List<string>();
    }
}