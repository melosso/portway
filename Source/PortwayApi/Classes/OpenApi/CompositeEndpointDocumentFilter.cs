using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using PortwayApi.Classes.Swagger;

namespace PortwayApi.Classes;

/// <summary>
/// This adds dynamic example loading while keeping all existing hardcoded examples as fallback
/// </summary>
public class CompositeEndpointDocumentFilter : IDocumentFilter
{
    private readonly ILogger<CompositeEndpointDocumentFilter> _logger;
    private readonly SwaggerExampleLoader _exampleLoader;

    public CompositeEndpointDocumentFilter(ILogger<CompositeEndpointDocumentFilter> logger)
    {
        _logger = logger;
        // Initialize example loader without passing logger (it creates its own if needed)
        _exampleLoader = new SwaggerExampleLoader();
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
                    Summary = definition.Documentation?.MethodDescriptions?.GetValueOrDefault("POST") 
                        ?? $"Execute {definition.EndpointName} composite endpoint",
                    Description = definition.Documentation?.MethodDocumentation?.GetValueOrDefault("POST") 
                        ?? definition.CompositeConfig?.Description 
                        ?? $"Executes the {definition.EndpointName} composite process with multiple steps",
                    OperationId = $"composite_{definition.FullPath}".Replace(" ", "_").Replace("/", "_"),
                    Parameters = new List<OpenApiParameter>()
                };

                // Add environment parameter
                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = "env",
                    In = ParameterLocation.Path,
                    Required = true,
                    Schema = new OpenApiSchema 
                    { 
                        Type = "string", 
                        Enum = allowedEnvironments.Select(e => new OpenApiString(e)).Cast<IOpenApiAny>().ToList() 
                    },
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

                // ==============================================================
                // NON-BREAKING CHANGE: Try dynamic example first, then fallback
                // ==============================================================
                
                var dynamicExample = TryLoadDynamicExample(definition, endpointKey);
                
                if (dynamicExample != null)
                {
                    // Use dynamically loaded example from file
                    operation.RequestBody.Content["application/json"].Example = dynamicExample;
                    _logger.LogDebug("Using dynamic example for composite endpoint: {EndpointName}", definition.EndpointName);
                }
                else if (string.Equals(definition.EndpointName, "SalesOrder", StringComparison.OrdinalIgnoreCase))
                {
                    // Fallback to existing hardcoded SalesOrder example
                    AddSalesOrderExample(operation, requestSchema);
                }
                else if (definition.CompositeConfig?.Steps != null)
                {
                    // Fallback to existing generic schema-based examples
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
                        else if (!string.IsNullOrEmpty(step.SourceProperty))
                        {
                            requestSchema.Properties[step.SourceProperty] = new OpenApiSchema
                            {
                                Type = "object",
                                Description = $"Data for {step.Name} step"
                            };
                        }
                    }
                }

                // Add responses (unchanged from original)
                operation.Responses = new OpenApiResponses
                {
                    ["200"] = new OpenApiResponse
                    {
                        Description = "Operation completed successfully",
                        Content = new Dictionary<string, OpenApiMediaType>
                        {
                            ["application/json"] = new OpenApiMediaType
                            {
                                Schema = new OpenApiSchema
                                {
                                    Type = "object",
                                    Properties = new Dictionary<string, OpenApiSchema>
                                    {
                                        { "success", new OpenApiSchema { Type = "boolean" } },
                                        { "stepResults", new OpenApiSchema { Type = "object" } }
                                    }
                                }
                            }
                        }
                    },
                    ["400"] = new OpenApiResponse { Description = "Bad request or validation error" },
                    ["401"] = new OpenApiResponse { Description = "Unauthorized - invalid or missing bearer token" },
                    ["404"] = new OpenApiResponse { Description = "Endpoint not found" }
                };

                // Add security requirement (unchanged from original)
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

            // Add all collected tags to the document (unchanged from original)
            if (documentTags.Any())
            {
                swaggerDoc.Tags ??= new List<OpenApiTag>();
                
                foreach (var tag in documentTags)
                {
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
            _logger.LogError(ex, "Error generating OpenAPI documentation for composite endpoints");
        }
    }

    /// <summary>
    /// Try to load dynamic example from file system. Returns null if file not found, allowing fallback to hardcoded examples
    /// </summary>
    private IOpenApiAny? TryLoadDynamicExample(EndpointDefinition definition, string endpointKey)
    {
        try
        {
            // Try loading with the full endpoint key path
            var example = _exampleLoader.LoadExample(endpointKey);
            if (example != null)
                return example;

            // Try namespace + endpoint name if available
            if (!string.IsNullOrEmpty(definition.EffectiveNamespace))
            {
                var namespacedPath = Path.Combine("Proxy", definition.EffectiveNamespace, definition.EndpointName);
                example = _exampleLoader.LoadExample(namespacedPath);
                if (example != null)
                    return example;
            }

            // Try simple endpoint name
            var simplePath = Path.Combine("Proxy", definition.EndpointName);
            return _exampleLoader.LoadExample(simplePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error loading dynamic example for: {EndpointName}, will use fallback", definition.EndpointName);
            return null;
        }
    }

    /// <summary>
    /// Specialized example for SalesOrder composite endpoint. This is kept as a fallback when no example.json file exists
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
                    ["Itemcode"] = new OpenApiString("ITEM-003"),
                    ["Quantity"] = new OpenApiInteger(5),
                    ["Price"] = new OpenApiFloat(0.0f)
                }
            }
        };
        
        // Add the example to the media type
        operation.RequestBody.Content["application/json"].Example = example;
    }

    /// <summary>
    /// Get allowed environments
    /// </summary>
    private List<string> GetAllowedEnvironments()
    {
        try
        {
            var environmentsPath = Path.Combine(Directory.GetCurrentDirectory(), "Environments");
            if (!Directory.Exists(environmentsPath))
            {
                return new List<string> { "demo" };
            }

            return Directory.GetDirectories(environmentsPath)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrEmpty(name))
                .Select(name => name!)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading allowed environments, using default");
            return new List<string> { "demo" };
        }
    }
}