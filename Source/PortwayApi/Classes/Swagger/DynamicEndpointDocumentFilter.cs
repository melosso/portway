using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Serilog;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace PortwayApi.Classes;

public class DynamicEndpointDocumentFilter : IDocumentFilter
{
    private readonly ILogger<DynamicEndpointDocumentFilter> _logger;

    public DynamicEndpointDocumentFilter(ILogger<DynamicEndpointDocumentFilter> logger)
    {
        _logger = logger;
    }

    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        _logger.LogDebug("DynamicEndpointDocumentFilter called");
        
        try {
            // Remove any controller-discovered paths that we'll be replacing
            RemoveConflictingPaths(swaggerDoc);
            
            // Generate unique operation IDs
            int operationIdCounter = 1;
            
            // Get allowed environments for parameter description
            var allowedEnvironments = GetAllowedEnvironments();
            
            // Collect all tags with descriptions for the document
            var documentTags = new Dictionary<string, string>();
            
            // Add documentation for each endpoint type
            AddSqlEndpoints(swaggerDoc, allowedEnvironments, ref operationIdCounter, documentTags);
            AddProxyEndpoints(swaggerDoc, allowedEnvironments, ref operationIdCounter, documentTags);
            AddWebhookEndpoints(swaggerDoc, allowedEnvironments, ref operationIdCounter);
            AddStaticEndpoints(swaggerDoc, allowedEnvironments, ref operationIdCounter, documentTags);
            
            // Collect file endpoint tags (but don't create operations since they're handled by EndpointController)
            CollectFileEndpointTags(documentTags);
            
            // Add all collected tags to the document with descriptions
            AddTagsToDocument(swaggerDoc, documentTags);

            // Ensure application/json is added automatically to all operations with request bodies
            foreach (var path in swaggerDoc.Paths)
            {
                foreach (var operation in path.Value.Operations)
                {
                    if (operation.Value.RequestBody != null && !operation.Value.RequestBody.Content.ContainsKey("application/json"))
                    {
                        operation.Value.RequestBody.Content["application/json"] = new OpenApiMediaType
                        {
                            Schema = new OpenApiSchema { Type = "object" }
                        };
                    }
                    
                    // Add Accept header for all operations (GET and POST)
                    if (!operation.Value.Parameters.Any(p => p.Name == "Accept"))
                    {
                        operation.Value.Parameters.Add(new OpenApiParameter
                        {
                            Name = "Accept",
                            In = ParameterLocation.Header,
                            Required = false,
                            Schema = new OpenApiSchema
                            {
                                Type = "string",
                                Default = new OpenApiString("application/json")
                            },
                            Description = "Specifies the media type of the response (default is application/json)"
                        });
                    }
                    
                    // Note: Content-Type header is automatically added by OpenAPI/Scalar based on RequestBody content
                    // so we don't need to add it explicitly as it would cause duplicates
                }
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error applying document filter");
        }
    }
    
    private void RemoveConflictingPaths(OpenApiDocument swaggerDoc)
    {
        var pathsToRemove = swaggerDoc.Paths
            .Where(p => p.Key.Contains("{catchall}") || p.Key.Contains("api/{env}"))
            .Select(p => p.Key)
            .ToList();
            
        foreach (var path in pathsToRemove)
        {
            swaggerDoc.Paths.Remove(path);
        }
    }
    
    private void AddSqlEndpoints(OpenApiDocument swaggerDoc, List<string> allowedEnvironments, ref int operationIdCounter, Dictionary<string, string> documentTags)
    {
        // Get SQL endpoints
        var sqlEndpoints = EndpointHandler.GetSqlEndpoints();
        
        foreach (var endpoint in sqlEndpoints)
        {
            string endpointName = endpoint.Key;
            var definition = endpoint.Value;
            
            // Collect tag description if provided
            if (!string.IsNullOrWhiteSpace(definition.Documentation?.TagDescription))
            {
                documentTags[endpointName] = definition.Documentation.TagDescription;
            }
            
            // Path template for this endpoint
            string path = $"/api/{{env}}/{endpointName}";
            
            // Create path item if it doesn't exist
            if (!swaggerDoc.Paths.ContainsKey(path))
            {
                swaggerDoc.Paths[path] = new OpenApiPathItem();
            }
            
            // Add operations based on allowed methods
            var methods = definition.Methods;
            
            // Ensure GET is always included (even if not specified)
            if (!methods.Contains("GET", StringComparer.OrdinalIgnoreCase))
            {
                methods.Add("GET");
            }
            
            // Add each allowed operation
            foreach (var method in methods)
            {
                var opType = GetOperationType(method);
                if (opType == null) continue;
                
                var operation = CreateSqlOperation(
                    endpointName, 
                    method, 
                    definition, 
                    allowedEnvironments, 
                    operationIdCounter++);
                    
                swaggerDoc.Paths[path].Operations[opType.Value] = operation;
            }
            
            // Add specific delete endpoint with ID parameter
            if (methods.Contains("DELETE", StringComparer.OrdinalIgnoreCase))
            {
                var deletePath = $"/api/{{env}}/{endpointName}";
                
                // Create path item if it doesn't exist
                if (!swaggerDoc.Paths.ContainsKey(deletePath))
                {
                    swaggerDoc.Paths[deletePath] = new OpenApiPathItem();
                }
                
                var deleteOperation = CreateSqlDeleteOperation(
                    endpointName, 
                    definition, 
                    allowedEnvironments, 
                    operationIdCounter++);
                    
                swaggerDoc.Paths[deletePath].Operations[OperationType.Delete] = deleteOperation;
            }
        }
    }
    
    private void AddProxyEndpoints(OpenApiDocument swaggerDoc, List<string> allowedEnvironments, ref int operationIdCounter, Dictionary<string, string> documentTags)
    {
        // Get proxy endpoints using the new method
        var proxyEndpoints = EndpointHandler.GetProxyEndpoints();
            
        foreach (var endpoint in proxyEndpoints)
        {
            string endpointName = endpoint.Key;
            var definition = endpoint.Value;
            
            // Skip private or composite endpoints
            if (definition.IsPrivate || definition.IsComposite)
                continue;
            
            // Collect tag description if provided
            if (!string.IsNullOrWhiteSpace(definition.Documentation?.TagDescription))
            {
                documentTags[endpointName] = definition.Documentation.TagDescription;
            }
                
            // Path template for this endpoint
            string path = $"/api/{{env}}/{endpointName}";
            
            // Create path item if it doesn't exist
            if (!swaggerDoc.Paths.ContainsKey(path))
            {
                swaggerDoc.Paths[path] = new OpenApiPathItem();
            }
            
            // Add operations for each HTTP method
            foreach (var method in definition.Methods)
            {
                var operation = new OpenApiOperation
                {
                    Tags = new List<OpenApiTag> { new OpenApiTag { Name = endpointName } },
                    Summary = GetOperationSummary(method, endpointName, definition),
                    Description = GetOperationDescription(method, endpointName, definition),
                    OperationId = $"{method.ToLower()}_{endpointName}".Replace(" ", "_"),
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

                // Add OData style query parameters for GET requests
                if (method.Equals("GET", StringComparison.OrdinalIgnoreCase))
                {
                    // Add $select parameter
                    operation.Parameters.Add(new OpenApiParameter
                    {
                        Name = "$select",
                        In = ParameterLocation.Query,
                        Required = false,
                        Schema = new OpenApiSchema { Type = "string" },
                        Description = "Select specific fields (comma-separated list of property names)"
                    });

                    // Add $top parameter with default value
                    operation.Parameters.Add(new OpenApiParameter
                    {
                        Name = "$top",
                        In = ParameterLocation.Query,
                        Required = false,
                        Schema = new OpenApiSchema { 
                            Type = "integer", 
                            Default = new OpenApiInteger(10),
                            Minimum = 1,
                            Maximum = 1000
                        },
                        Description = "Limit the number of results returned (default: 10, max: 1000)"
                    });

                    // Add $filter parameter
                    operation.Parameters.Add(new OpenApiParameter
                    {
                        Name = "$filter",
                        In = ParameterLocation.Query,
                        Required = false,
                        Schema = new OpenApiSchema { Type = "string" },
                        Description = "Filter the results based on a condition (e.g., Name eq 'Value')"
                    });
                }
                
                // Add request body for methods that support it
                if (method.Equals("POST", StringComparison.OrdinalIgnoreCase) || 
                    method.Equals("PUT", StringComparison.OrdinalIgnoreCase) ||
                    method.Equals("PATCH", StringComparison.OrdinalIgnoreCase) ||
                    method.Equals("MERGE", StringComparison.OrdinalIgnoreCase))
                {
                    operation.RequestBody = new OpenApiRequestBody
                    {
                        Required = true,
                        Content = new Dictionary<string, OpenApiMediaType>
                        {
                            ["application/json"] = new OpenApiMediaType
                            {
                                Schema = new OpenApiSchema { Type = "object" }
                            },
                            ["text/xml"] = new OpenApiMediaType
                            {
                                Schema = new OpenApiSchema { Type = "string" }
                            },
                            ["application/soap+xml"] = new OpenApiMediaType
                            {
                                Schema = new OpenApiSchema { Type = "string" }
                            },
                            ["application/xml"] = new OpenApiMediaType
                            {
                                Schema = new OpenApiSchema { Type = "string" }
                            },
                            ["text/plain"] = new OpenApiMediaType
                            {
                                Schema = new OpenApiSchema { Type = "string" }
                            }
                        }
                    };
                }

                // Add example response
                operation.Responses = new OpenApiResponses
                {
                    ["200"] = new OpenApiResponse 
                    { 
                        Description = "Successful response",
                        Content = new Dictionary<string, OpenApiMediaType>
                        {
                            ["application/json"] = new OpenApiMediaType
                            {
                                Schema = new OpenApiSchema { Type = "object" }
                            }
                        }
                    },
                    ["400"] = new OpenApiResponse { Description = "Bad Request - Invalid request" },
                    ["401"] = new OpenApiResponse { Description = "Unauthorized - Missing or invalid authentication token" },
                    ["403"] = new OpenApiResponse { Description = "Forbidden - Insufficient permissions" },
                    ["404"] = new OpenApiResponse { Description = "Not Found - Resource not found" },
                    ["500"] = new OpenApiResponse { Description = "Internal Server Error" }
                };

                // Add the operation to the path with the appropriate HTTP method
                AddOperationToPath(swaggerDoc.Paths[path], method, operation);
            }
        }
    }
    
    private void AddWebhookEndpoints(OpenApiDocument swaggerDoc, List<string> allowedEnvironments, ref int operationIdCounter)
    {
        // Add webhook endpoint with correct path pattern
        string path = "/api/{env}/webhook/{webhookId}";

        var webhookEndpoints = EndpointHandler.GetSqlWebhookEndpoints();
        if (webhookEndpoints == null || webhookEndpoints.Count == 0)
        {
            return; // Skip adding webhook endpoints to Swagger if none exist
        }
        
        // Load webhook documentation from entity.json
        var webhookDocumentation = LoadWebhookDocumentation();
        
        // Create path item if it doesn't exist
        if (!swaggerDoc.Paths.ContainsKey(path))
        {
            swaggerDoc.Paths[path] = new OpenApiPathItem();
        }
        
        // Create webhook POST operation
        var webhookOperation = new OpenApiOperation
        {
            Tags = new List<OpenApiTag> { new() { Name = "Webhook" } },
            Summary = webhookDocumentation?.MethodDescriptions?.GetValueOrDefault("POST") ?? "Process incoming request",
            Description = webhookDocumentation?.MethodDocumentation?.GetValueOrDefault("POST") ?? "Receives and processes a request payload",
            OperationId = $"op_{operationIdCounter++}",
            Parameters = new List<OpenApiParameter>
            {
                new()
                {
                    Name = "env",
                    In = ParameterLocation.Path,
                    Required = true,
                    Schema = new OpenApiSchema
                    {
                        Type = "string",
                        Enum = allowedEnvironments.Select(e => new OpenApiString(e)).Cast<IOpenApiAny>().ToList()
                    },
                    Description = "Target environment"
                },
                new()
                {
                    Name = "webhookId",
                    In = ParameterLocation.Path,
                    Required = true,
                    Schema = new OpenApiSchema { Type = "string" },
                    Description = "Webhook identifier"
                }
            },
            RequestBody = new OpenApiRequestBody
            {
                Description = "Webhook payload (any valid JSON)",
                Required = true,
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = "object"
                        }
                    }
                }
            },
            Responses = new OpenApiResponses
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
                                    ["message"] = new OpenApiSchema { Type = "string", Example = new OpenApiString("Request processed successfully.") },
                                    ["id"] = new OpenApiSchema { Type = "integer", Example = new OpenApiInteger(12345) }
                                }
                            },
                            Example = new OpenApiObject
                            {
                                ["message"] = new OpenApiString("Webhook processed successfully."),
                                ["id"] = new OpenApiInteger(12345)
                            }
                        }
                    }
                },
                ["400"] = new OpenApiResponse 
                { 
                    Description = "Bad Request - Invalid request",
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new OpenApiMediaType
                        {
                            Schema = new OpenApiSchema
                            {
                                Type = "object",
                                Properties = new Dictionary<string, OpenApiSchema>
                                {
                                    ["error"] = new OpenApiSchema { Type = "string" },
                                    ["success"] = new OpenApiSchema { Type = "boolean", Example = new OpenApiBoolean(false) }
                                }
                            },
                            Example = new OpenApiObject
                            {
                                ["error"] = new OpenApiString("Environment is not configured properly."),
                                ["success"] = new OpenApiBoolean(false)
                            }
                        }
                    }
                },
                ["401"] = new OpenApiResponse 
                { 
                    Description = "Unauthorized - Missing or invalid authentication token",
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new OpenApiMediaType
                        {
                            Schema = new OpenApiSchema
                            {
                                Type = "object",
                                Properties = new Dictionary<string, OpenApiSchema>
                                {
                                    ["error"] = new OpenApiSchema { Type = "string" }
                                }
                            },
                            Example = new OpenApiObject
                            {
                                ["error"] = new OpenApiString("Unauthorized access. Valid authentication token required.")
                            }
                        }
                    }
                },
                ["403"] = new OpenApiResponse 
                { 
                    Description = "Forbidden - Token valid but insufficient permissions",
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new OpenApiMediaType
                        {
                            Schema = new OpenApiSchema
                            {
                                Type = "object",
                                Properties = new Dictionary<string, OpenApiSchema>
                                {
                                    ["error"] = new OpenApiSchema { Type = "string" }
                                }
                            },
                            Example = new OpenApiObject
                            {
                                ["error"] = new OpenApiString("Environment 'production' is not allowed.")
                            }
                        }
                    }
                },
                ["404"] = new OpenApiResponse 
                { 
                    Description = "Not Found - Resource not found or not configured",
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new OpenApiMediaType
                        {
                            Schema = new OpenApiSchema
                            {
                                Type = "object",
                                Properties = new Dictionary<string, OpenApiSchema>
                                {
                                    ["error"] = new OpenApiSchema { Type = "string" },
                                    ["success"] = new OpenApiSchema { Type = "boolean", Example = new OpenApiBoolean(false) }
                                }
                            },
                            Example = new OpenApiObject
                            {
                                ["error"] = new OpenApiString("Webhook ID 'unknown_webhook' is not configured."),
                                ["success"] = new OpenApiBoolean(false)
                            }
                        }
                    }
                },
                ["500"] = new OpenApiResponse 
                { 
                    Description = "Internal Server Error",
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new OpenApiMediaType
                        {
                            Schema = new OpenApiSchema
                            {
                                Type = "object",
                                Properties = new Dictionary<string, OpenApiSchema>
                                {
                                    ["type"] = new OpenApiSchema { Type = "string" },
                                    ["title"] = new OpenApiSchema { Type = "string" },
                                    ["status"] = new OpenApiSchema { Type = "integer" },
                                    ["detail"] = new OpenApiSchema { Type = "string" }
                                }
                            },
                            Example = new OpenApiObject
                            {
                                ["type"] = new OpenApiString("https://tools.ietf.org/html/rfc7231#section-6.6.1"),
                                ["title"] = new OpenApiString("Error"),
                                ["status"] = new OpenApiInteger(500),
                                ["detail"] = new OpenApiString("Error processing. Please check the logs for more details.")
                            }
                        }
                    }
                }
            }
        };
        
        swaggerDoc.Paths[path].Operations[OperationType.Post] = webhookOperation;
    }
    
    /// <summary>
    /// Collects file endpoint tags for documentation (operations are handled by EndpointController)
    /// </summary>
    private void CollectFileEndpointTags(Dictionary<string, string> documentTags)
    {
        // Get file endpoints and collect their tag descriptions
        var fileEndpoints = EndpointHandler.GetFileEndpoints();
        
        foreach (var endpoint in fileEndpoints)
        {
            string endpointName = endpoint.Key;
            var definition = endpoint.Value;
            
            // Skip private endpoints
            if (definition.IsPrivate)
                continue;
            
            // Collect tag description if provided
            if (!string.IsNullOrWhiteSpace(definition.Documentation?.TagDescription))
            {
                documentTags[endpointName] = definition.Documentation.TagDescription;
            }
        }
    }
    
    /// <summary>
    /// Adds static endpoints to the OpenAPI document
    /// </summary>
    private void AddStaticEndpoints(OpenApiDocument swaggerDoc, List<string> allowedEnvironments, ref int operationIdCounter, Dictionary<string, string> documentTags)
    {
        var staticEndpoints = EndpointHandler.GetStaticEndpoints();
        
        foreach (var endpoint in staticEndpoints)
        {
            string endpointName = endpoint.Key;
            var definition = endpoint.Value;
            
            // Skip private endpoints
            if (definition.IsPrivate)
                continue;
            
            // Collect tag description
            if (!string.IsNullOrWhiteSpace(definition.Documentation?.TagDescription))
            {
                documentTags[endpointName] = definition.Documentation.TagDescription;
            }
            
            // Get content type and filtering capability
            var contentType = definition.Properties?.GetValueOrDefault("ContentType", "text/plain")?.ToString() ?? "text/plain";
            var enableFiltering = (bool)(definition.Properties?.GetValueOrDefault("EnableFiltering", false) ?? false);
            
            // Create single OpenAPI path with environment parameter
            string path = $"/api/{{env}}/{endpointName}";
            
            if (!swaggerDoc.Paths.ContainsKey(path))
            {
                swaggerDoc.Paths[path] = new OpenApiPathItem();
            }
                
            // Add GET operation
            var getOperation = new OpenApiOperation
            {
                OperationId = $"get{endpointName}Static{operationIdCounter++}",
                Summary = definition.Documentation?.MethodDescriptions?.GetValueOrDefault("GET") ?? $"Get content from {endpointName}",
                Description = GetStaticOperationDescription("GET", endpointName, definition, contentType),
                Tags = new List<OpenApiTag> { new OpenApiTag { Name = endpointName } },
                Parameters = new List<OpenApiParameter>
                {
                    // Environment parameter
                    new OpenApiParameter
                    {
                        Name = "env",
                        In = ParameterLocation.Path,
                        Required = true,
                        Schema = new OpenApiSchema
                        {
                            Type = "string",
                            Enum = allowedEnvironments.Select(e => new OpenApiString(e)).Cast<IOpenApiAny>().ToList()
                        },
                        Description = "Environment identifier"
                    },
                    // Accept header parameter with the correct content type
                    new OpenApiParameter
                    {
                        Name = "Accept",
                        In = ParameterLocation.Header,
                        Required = false,
                        Schema = new OpenApiSchema
                        {
                            Type = "string",
                            Default = new OpenApiString(contentType)
                        },
                        Description = $"Specifies the media type of the response (default is {contentType})"
                    }
                },
                Responses = new OpenApiResponses
                {
                    ["200"] = new OpenApiResponse
                    {
                        Description = "Successful response",
                        Content = new Dictionary<string, OpenApiMediaType>
                        {
                            [contentType] = new OpenApiMediaType
                            {
                                Schema = contentType.Contains("json") 
                                    ? new OpenApiSchema { Type = "object", Description = "JSON content" }
                                    : new OpenApiSchema { Type = "string", Description = "Content" },
                                Examples = contentType.Contains("json") 
                                    ? new Dictionary<string, OpenApiExample>
                                    {
                                        ["sample_data"] = new OpenApiExample
                                        {
                                            Summary = "Sample JSON data",
                                            Value = new OpenApiArray
                                            {
                                                new OpenApiObject()
                                            }
                                        }
                                    }
                                    : new Dictionary<string, OpenApiExample>
                                    {
                                        ["sample_text"] = new OpenApiExample
                                        {
                                            Summary = "Sample text content",
                                            Value = new OpenApiString("Hello world")
                                        }
                                    }
                            }
                        }
                    },
                    ["304"] = new OpenApiResponse
                    {
                        Description = "Not Modified - Content has not changed since last request",
                        Headers = new Dictionary<string, OpenApiHeader>
                        {
                            ["Last-Modified"] = new OpenApiHeader
                            {
                                Description = "Date when the content was last modified",
                                Schema = new OpenApiSchema { Type = "string", Format = "date-time" }
                            },
                            ["ETag"] = new OpenApiHeader
                            {
                                Description = "Entity tag for the current version of the content",
                                Schema = new OpenApiSchema { Type = "string" }
                            }
                        }
                    },
                    ["401"] = new OpenApiResponse
                    {
                        Description = "Unauthorized - Missing or invalid authentication token",
                        Content = new Dictionary<string, OpenApiMediaType>
                        {
                            ["application/json"] = new OpenApiMediaType
                            {
                                Schema = new OpenApiSchema
                                {
                                    Type = "object",
                                    Properties = new Dictionary<string, OpenApiSchema>
                                    {
                                        ["error"] = new OpenApiSchema { Type = "string" }
                                    }
                                },
                                Example = new OpenApiObject
                                {
                                    ["error"] = new OpenApiString("Unauthorized access. Valid Bearer token required.")
                                }
                            }
                        }
                    },
                    ["403"] = new OpenApiResponse
                    {
                        Description = "Forbidden - Insufficient permissions",
                        Content = new Dictionary<string, OpenApiMediaType>
                        {
                            ["application/json"] = new OpenApiMediaType
                            {
                                Schema = new OpenApiSchema
                                {
                                    Type = "object",
                                    Properties = new Dictionary<string, OpenApiSchema>
                                    {
                                        ["error"] = new OpenApiSchema { Type = "string" }
                                    }
                                },
                                Example = new OpenApiObject
                                {
                                    ["error"] = new OpenApiString("Environment 'production' is not allowed for this endpoint")
                                }
                            }
                        }
                    },
                    ["404"] = new OpenApiResponse
                    {
                        Description = "Not Found - Resource not found",
                        Content = new Dictionary<string, OpenApiMediaType>
                        {
                            ["application/json"] = new OpenApiMediaType
                            {
                                Schema = new OpenApiSchema
                                {
                                    Type = "object",
                                    Properties = new Dictionary<string, OpenApiSchema>
                                    {
                                        ["error"] = new OpenApiSchema { Type = "string" }
                                    }
                                },
                                Examples = new Dictionary<string, OpenApiExample>
                                {
                                    ["endpoint_not_found"] = new OpenApiExample
                                    {
                                        Summary = "Endpoint not found",
                                        Value = new OpenApiObject
                                        {
                                            ["error"] = new OpenApiString($"Endpoint '{endpointName}' not found")
                                        }
                                    },
                                    ["content_not_found"] = new OpenApiExample
                                    {
                                        Summary = "Content not found",
                                        Value = new OpenApiObject
                                        {
                                            ["error"] = new OpenApiString("Content not found")
                                        }
                                    }
                                }
                            }
                        }
                    },
                    ["406"] = new OpenApiResponse
                    {
                        Description = "Not Acceptable - Content negotiation failed",
                        Content = new Dictionary<string, OpenApiMediaType>
                        {
                            ["application/json"] = new OpenApiMediaType
                            {
                                Schema = new OpenApiSchema
                                {
                                    Type = "object",
                                    Properties = new Dictionary<string, OpenApiSchema>
                                    {
                                        ["error"] = new OpenApiSchema { Type = "string" },
                                        ["detail"] = new OpenApiSchema { Type = "string" },
                                        ["availableContentType"] = new OpenApiSchema { Type = "string" }
                                    }
                                },
                                Example = new OpenApiObject
                                {
                                    ["error"] = new OpenApiString("Not Acceptable"),
                                    ["detail"] = new OpenApiString($"Endpoint provides '{contentType}' but client accepts 'text/html'"),
                                    ["availableContentType"] = new OpenApiString(contentType)
                                }
                            }
                        }
                    },
                    ["500"] = new OpenApiResponse
                    {
                        Description = "Internal Server Error",
                        Content = new Dictionary<string, OpenApiMediaType>
                        {
                            ["application/json"] = new OpenApiMediaType
                            {
                                Schema = new OpenApiSchema
                                {
                                    Type = "object",
                                    Properties = new Dictionary<string, OpenApiSchema>
                                    {
                                        ["error"] = new OpenApiSchema { Type = "string" }
                                    }
                                },
                                Example = new OpenApiObject
                                {
                                    ["error"] = new OpenApiString("Error serving content")
                                }
                            }
                        }
                    }
                }
            };
            
            // Add OData parameters if filtering is enabled and content supports filtering
            if (enableFiltering && (contentType.Contains("json") || contentType.Contains("xml")))
            {
                getOperation.Parameters.Add(new OpenApiParameter
                {
                    Name = "$select",
                    In = ParameterLocation.Query,
                    Required = false,
                    Schema = new OpenApiSchema { Type = "string" },
                    Description = "Select specific fields (OData $select)"
                });
                
                getOperation.Parameters.Add(new OpenApiParameter
                {
                    Name = "$filter",
                    In = ParameterLocation.Query,
                    Required = false,
                    Schema = new OpenApiSchema { Type = "string" },
                    Description = "Filter results (OData $filter)"
                });
                
                getOperation.Parameters.Add(new OpenApiParameter
                {
                    Name = "$orderby",
                    In = ParameterLocation.Query,
                    Required = false,
                    Schema = new OpenApiSchema { Type = "string" },
                    Description = "Sort results (OData $orderby)"
                });
                
                getOperation.Parameters.Add(new OpenApiParameter
                {
                    Name = "$top",
                    In = ParameterLocation.Query,
                    Required = false,
                    Schema = new OpenApiSchema { Type = "integer", Default = new OpenApiInteger(10) },
                    Description = "Maximum number of results (OData $top)"
                });
                
                getOperation.Parameters.Add(new OpenApiParameter
                {
                    Name = "$skip",
                    In = ParameterLocation.Query,
                    Required = false,
                    Schema = new OpenApiSchema { Type = "integer", Default = new OpenApiInteger(0) },
                    Description = "Number of results to skip (OData $skip)"
                });
            }
            
            swaggerDoc.Paths[path].Operations[OperationType.Get] = getOperation;
        }
    }
    
    private OpenApiOperation CreateSqlOperation(
        string endpointName, 
        string method, 
        EndpointDefinition definition,
        List<string> allowedEnvironments,
        int operationId)
    {
        var operation = new OpenApiOperation
        {
            Tags = new List<OpenApiTag> { new() { Name = endpointName } }, // Assign unique tag based on endpoint name
            Summary = GetOperationSummary(method, endpointName, definition),
            Description = GetOperationDescription(method, endpointName, definition),
            OperationId = $"op_{operationId}",
            Parameters = new List<OpenApiParameter>
            {
                // Environment parameter
                new()
                {
                    Name = "env",
                    In = ParameterLocation.Path,
                    Required = true,
                    Schema = new OpenApiSchema
                    {
                        Type = "string",
                        Enum = allowedEnvironments.Select(e => new OpenApiString(e)).Cast<IOpenApiAny>().ToList()
                    },
                    Description = "Target environment"
                }
            }
        };
        
        // Add method-specific parameters and request body
        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase))
        {
            // Add GET-specific parameters
            foreach (var parameter in new List<OpenApiParameter>
            {
                new()
                {
                    Name = "$select",
                    In = ParameterLocation.Query,
                    Required = false,
                    Schema = new OpenApiSchema { Type = "string" },
                    Description = "Select specific fields (comma-separated list of property names)"
                },
                new()
                {
                    Name = "$filter",
                    In = ParameterLocation.Query,
                    Required = false,
                    Schema = new OpenApiSchema { Type = "string" },
                    Description = "OData $filter expression"
                },
                new()
                {
                    Name = "$orderby",
                    In = ParameterLocation.Query,
                    Required = false,
                    Schema = new OpenApiSchema { Type = "string" },
                    Description = "OData $orderby expression"
                },
                new()
                {
                    Name = "$top",
                    In = ParameterLocation.Query,
                    Required = false,
                    Schema = new OpenApiSchema { 
                        Type = "integer", 
                        Default = new OpenApiInteger(10) 
                    },
                    Description = "Maximum number of records to return"
                },
                new()
                {
                    Name = "$skip",
                    In = ParameterLocation.Query,
                    Required = false,
                    Schema = new OpenApiSchema { 
                        Type = "integer", 
                        Default = new OpenApiInteger(0) 
                    },
                    Description = "Number of records to skip"
                }
            })
            {
                operation.Parameters.Add(parameter);
            }
        }
        else if (method.Equals("POST", StringComparison.OrdinalIgnoreCase) || 
                 method.Equals("PUT", StringComparison.OrdinalIgnoreCase) ||
                 method.Equals("MERGE", StringComparison.OrdinalIgnoreCase))
        {
            // Add request body for POST and PUT
            operation.RequestBody = new OpenApiRequestBody
            {
                Description = method.Equals("POST") ? "Data for new record" : "Data for updated record",
                Required = true,
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = "object"
                        }
                    }
                }
            };
        }
        
        // Add comprehensive responses with detailed schemas and examples  
        operation.Responses = new OpenApiResponses
        {
            ["200"] = new OpenApiResponse 
            { 
                Description = method switch
                {
                    "GET" => "Successful response",
                    "POST" => "Successful response",
                    "PUT" => "Successful response",
                    "MERGE" => "Successful response",
                    "DELETE" => "Successful response",
                    _ => "Successful response"
                },
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = method switch
                        {
                            "GET" => new OpenApiSchema
                            {
                                Type = "object",
                                Properties = new Dictionary<string, OpenApiSchema>
                                {
                                    ["count"] = new OpenApiSchema { Type = "integer" },
                                    ["value"] = new OpenApiSchema 
                                    { 
                                        Type = "array",
                                        Items = new OpenApiSchema { Type = "object" }
                                    },
                                    ["nextLink"] = new OpenApiSchema { Type = "string" }
                                }
                            },
                            "POST" => new OpenApiSchema
                            {
                                Type = "object",
                                Properties = new Dictionary<string, OpenApiSchema>
                                {
                                    ["success"] = new OpenApiSchema { Type = "boolean" },
                                    ["id"] = new OpenApiSchema { Type = "integer" },
                                    ["message"] = new OpenApiSchema { Type = "string" }
                                }
                            },
                            "PUT" or "MERGE" => new OpenApiSchema
                            {
                                Type = "object",
                                Properties = new Dictionary<string, OpenApiSchema>
                                {
                                    ["success"] = new OpenApiSchema { Type = "boolean" },
                                    ["rowsAffected"] = new OpenApiSchema { Type = "integer" },
                                    ["message"] = new OpenApiSchema { Type = "string" }
                                }
                            },
                            _ => new OpenApiSchema { Type = "object" }
                        },
                        Examples = method switch
                        {
                            "GET" => new Dictionary<string, OpenApiExample>
                            {
                                ["success"] = new OpenApiExample
                                {
                                    Summary = "Successful data retrieval",
                                    Value = new OpenApiObject
                                    {
                                        ["count"] = new OpenApiInteger(0),
                                        ["value"] = new OpenApiArray(),
                                        ["nextLink"] = new OpenApiString($"/api/{endpointName}?$top=10&$skip=0")
                                    }
                                }
                            },
                            "POST" => new Dictionary<string, OpenApiExample>
                            {
                                ["success"] = new OpenApiExample
                                {
                                    Summary = "Successful creation",
                                    Value = new OpenApiObject
                                    {
                                        ["success"] = new OpenApiBoolean(true),
                                        ["id"] = new OpenApiInteger(123),
                                        ["message"] = new OpenApiString("Record created successfully")
                                    }
                                }
                            },
                            "PUT" or "MERGE" => new Dictionary<string, OpenApiExample>
                            {
                                ["success"] = new OpenApiExample
                                {
                                    Summary = "Successful update",
                                    Value = new OpenApiObject
                                    {
                                        ["success"] = new OpenApiBoolean(true),
                                        ["rowsAffected"] = new OpenApiInteger(1),
                                        ["message"] = new OpenApiString("Record updated successfully")
                                    }
                                }
                            },
                            _ => new Dictionary<string, OpenApiExample>()
                        }
                    }
                }
            },
            ["400"] = new OpenApiResponse 
            { 
                Description = "Bad Request - Invalid request",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, OpenApiSchema>
                            {
                                ["error"] = new OpenApiSchema { Type = "string" },
                                ["details"] = new OpenApiSchema { Type = "string" }
                            }
                        },
                        Examples = new Dictionary<string, OpenApiExample>
                        {
                            ["invalid_odata"] = new OpenApiExample
                            {
                                Summary = "Invalid OData syntax",
                                Value = new OpenApiObject
                                {
                                    ["error"] = new OpenApiString("Invalid OData filter syntax"),
                                    ["details"] = new OpenApiString("Syntax error in $filter expression near 'invalidField'")
                                }
                            },
                            ["missing_required"] = new OpenApiExample
                            {
                                Summary = "Missing required field",
                                Value = new OpenApiObject
                                {
                                    ["error"] = new OpenApiString("Required field 'name' is missing"),
                                    ["details"] = new OpenApiString("POST request must include 'name' field in request body")
                                }
                            }
                        }
                    }
                }
            },
            ["401"] = new OpenApiResponse 
            { 
                Description = "Unauthorized - Missing or invalid authentication token",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, OpenApiSchema>
                            {
                                ["error"] = new OpenApiSchema { Type = "string" }
                            }
                        },
                        Example = new OpenApiObject
                        {
                            ["error"] = new OpenApiString("Unauthorized access. Valid Bearer token required.")
                        }
                    }
                }
            },
            ["403"] = new OpenApiResponse 
            { 
                Description = "Forbidden - Insufficient permissions",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, OpenApiSchema>
                            {
                                ["error"] = new OpenApiSchema { Type = "string" }
                            }
                        },
                        Examples = new Dictionary<string, OpenApiExample>
                        {
                            ["environment_restricted"] = new OpenApiExample
                            {
                                Summary = "Environment access denied",
                                Value = new OpenApiObject
                                {
                                    ["error"] = new OpenApiString("Environment 'production' is not allowed for this endpoint")
                                }
                            },
                            ["method_not_allowed"] = new OpenApiExample
                            {
                                Summary = "Method not allowed",
                                Value = new OpenApiObject
                                {
                                    ["error"] = new OpenApiString("DELETE method is not allowed for this endpoint")
                                }
                            }
                        }
                    }
                }
            },
            ["404"] = new OpenApiResponse 
            { 
                Description = "Not Found - Resource not found",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, OpenApiSchema>
                            {
                                ["error"] = new OpenApiSchema { Type = "string" }
                            }
                        },
                        Examples = new Dictionary<string, OpenApiExample>
                        {
                            ["endpoint_not_found"] = new OpenApiExample
                            {
                                Summary = "Endpoint not found",
                                Value = new OpenApiObject
                                {
                                    ["error"] = new OpenApiString($"Endpoint '{endpointName}' not found")
                                }
                            },
                            ["record_not_found"] = new OpenApiExample
                            {
                                Summary = "Record not found",
                                Value = new OpenApiObject
                                {
                                    ["error"] = new OpenApiString("Record with specified ID not found")
                                }
                            }
                        }
                    }
                }
            },
            ["409"] = new OpenApiResponse 
            { 
                Description = "Conflict - Duplicate key violation or business rule conflict",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, OpenApiSchema>
                            {
                                ["error"] = new OpenApiSchema { Type = "string" },
                                ["conflictType"] = new OpenApiSchema { Type = "string" },
                                ["details"] = new OpenApiSchema { Type = "string" }
                            }
                        },
                        Example = new OpenApiObject
                        {
                            ["error"] = new OpenApiString("Record with this identifier already exists"),
                            ["conflictType"] = new OpenApiString("CONSTRAINT_VIOLATION"),
                            ["details"] = new OpenApiString("Violation of the constraint 'UK_Name'")
                        }
                    }
                }
            },
            ["422"] = new OpenApiResponse 
            { 
                Description = "Unprocessable Entity - Validation errors or business rule violations",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, OpenApiSchema>
                            {
                                ["error"] = new OpenApiSchema { Type = "string" },
                                ["validationErrors"] = new OpenApiSchema 
                                { 
                                    Type = "array",
                                    Items = new OpenApiSchema
                                    {
                                        Type = "object",
                                        Properties = new Dictionary<string, OpenApiSchema>
                                        {
                                            ["field"] = new OpenApiSchema { Type = "string" },
                                            ["message"] = new OpenApiSchema { Type = "string" }
                                        }
                                    }
                                }
                            }
                        },
                        Example = new OpenApiObject
                        {
                            ["error"] = new OpenApiString("Validation failed"),
                            ["validationErrors"] = new OpenApiArray
                            {
                                new OpenApiObject
                                {
                                    ["field"] = new OpenApiString("email"),
                                    ["message"] = new OpenApiString("Invalid email format")
                                },
                                new OpenApiObject
                                {
                                    ["field"] = new OpenApiString("age"),
                                    ["message"] = new OpenApiString("Age must be between 18 and 120")
                                }
                            }
                        }
                    }
                }
            },
            ["500"] = new OpenApiResponse 
            { 
                Description = "Internal Server Error",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, OpenApiSchema>
                            {
                                ["type"] = new OpenApiSchema { Type = "string" },
                                ["title"] = new OpenApiSchema { Type = "string" },
                                ["status"] = new OpenApiSchema { Type = "integer" },
                                ["detail"] = new OpenApiSchema { Type = "string" },
                                ["traceId"] = new OpenApiSchema { Type = "string" }
                            }
                        },
                        Example = new OpenApiObject
                        {
                            ["type"] = new OpenApiString("https://tools.ietf.org/html/rfc7231#section-6.6.1"),
                            ["title"] = new OpenApiString("Internal Server Error"),
                            ["status"] = new OpenApiInteger(500),
                            ["detail"] = new OpenApiString("An internal error occurred. Please check the logs for more details."),
                            ["traceId"] = new OpenApiString("value")
                        }
                    }
                }
            }
        };
        
        return operation;
    }
    
    private OpenApiOperation CreateSqlDeleteOperation(
        string endpointName, 
        EndpointDefinition definition,
        List<string> allowedEnvironments,
        int operationId)
    {
        var operation = new OpenApiOperation
        {
            Tags = new List<OpenApiTag> { new() { Name = endpointName } }, // Assign unique tag based on endpoint name
            Summary = GetOperationSummary("DELETE", endpointName, definition),
            Description = GetOperationDescription("DELETE", endpointName, definition),
            OperationId = $"op_{operationId}",
            Parameters = new List<OpenApiParameter>
            {
                // Environment parameter
                new()
                {
                    Name = "env",
                    In = ParameterLocation.Path,
                    Required = true,
                    Schema = new OpenApiSchema
                    {
                        Type = "string",
                        Enum = allowedEnvironments.Select(e => new OpenApiString(e)).Cast<IOpenApiAny>().ToList()
                    },
                    Description = "Target environment"
                },
                // ID parameter
                new()
                {
                    Name = "id",
                    In = ParameterLocation.Query,
                    Required = true,
                    Schema = new OpenApiSchema { Type = "string" },
                    Description = "ID of the record to delete"
                }
            },
            Responses = new OpenApiResponses
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
                                    ["success"] = new OpenApiSchema { Type = "boolean" },
                                    ["rowsAffected"] = new OpenApiSchema { Type = "integer" },
                                    ["message"] = new OpenApiSchema { Type = "string" }
                                }
                            },
                            Example = new OpenApiObject
                            {
                                ["success"] = new OpenApiBoolean(true),
                                ["rowsAffected"] = new OpenApiInteger(1),
                                ["message"] = new OpenApiString("Record deleted successfully")
                            }
                        }
                    }
                },
                ["400"] = new OpenApiResponse 
                { 
                    Description = "Bad Request - Invalid request",
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new OpenApiMediaType
                        {
                            Schema = new OpenApiSchema
                            {
                                Type = "object",
                                Properties = new Dictionary<string, OpenApiSchema>
                                {
                                    ["error"] = new OpenApiSchema { Type = "string" }
                                }
                            },
                            Example = new OpenApiObject
                            {
                                ["error"] = new OpenApiString("ID parameter is required for delete operation")
                            }
                        }
                    }
                },
                ["401"] = new OpenApiResponse 
                { 
                    Description = "Unauthorized - Missing or invalid authentication token",
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new OpenApiMediaType
                        {
                            Schema = new OpenApiSchema
                            {
                                Type = "object",
                                Properties = new Dictionary<string, OpenApiSchema>
                                {
                                    ["error"] = new OpenApiSchema { Type = "string" }
                                }
                            },
                            Example = new OpenApiObject
                            {
                                ["error"] = new OpenApiString("Unauthorized access. Valid Bearer token required.")
                            }
                        }
                    }
                },
                ["403"] = new OpenApiResponse 
                { 
                    Description = "Forbidden - Insufficient permissions",
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new OpenApiMediaType
                        {
                            Schema = new OpenApiSchema
                            {
                                Type = "object",
                                Properties = new Dictionary<string, OpenApiSchema>
                                {
                                    ["error"] = new OpenApiSchema { Type = "string" }
                                }
                            },
                            Example = new OpenApiObject
                            {
                                ["error"] = new OpenApiString("DELETE method is not allowed for this endpoint")
                            }
                        }
                    }
                },
                ["404"] = new OpenApiResponse 
                { 
                    Description = "Not Found - Resource not found",
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new OpenApiMediaType
                        {
                            Schema = new OpenApiSchema
                            {
                                Type = "object",
                                Properties = new Dictionary<string, OpenApiSchema>
                                {
                                    ["error"] = new OpenApiSchema { Type = "string" }
                                }
                            },
                            Examples = new Dictionary<string, OpenApiExample>
                            {
                                ["endpoint_not_found"] = new OpenApiExample
                                {
                                    Summary = "Endpoint not found",
                                    Value = new OpenApiObject
                                    {
                                        ["error"] = new OpenApiString($"Endpoint '{endpointName}' not found")
                                    }
                                },
                                ["record_not_found"] = new OpenApiExample
                                {
                                    Summary = "Record not found",
                                    Value = new OpenApiObject
                                    {
                                        ["error"] = new OpenApiString("Record with specified ID not found")
                                    }
                                }
                            }
                        }
                    }
                },
                ["500"] = new OpenApiResponse 
                { 
                    Description = "Internal Server Error",
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new OpenApiMediaType
                        {
                            Schema = new OpenApiSchema
                            {
                                Type = "object",
                                Properties = new Dictionary<string, OpenApiSchema>
                                {
                                    ["type"] = new OpenApiSchema { Type = "string" },
                                    ["title"] = new OpenApiSchema { Type = "string" },
                                    ["status"] = new OpenApiSchema { Type = "integer" },
                                    ["detail"] = new OpenApiSchema { Type = "string" },
                                    ["traceId"] = new OpenApiSchema { Type = "string" }
                                }
                            },
                            Example = new OpenApiObject
                            {
                                ["type"] = new OpenApiString("https://tools.ietf.org/html/rfc7231#section-6.6.1"),
                                ["title"] = new OpenApiString("Internal Server Error"),
                                ["status"] = new OpenApiInteger(500),
                                ["detail"] = new OpenApiString("An internal error occurred. Please check the logs for more details."),
                                ["traceId"] = new OpenApiString("value")
                            }
                        }
                    }
                }
            }
        };
        
        return operation;
    }
    
    private OpenApiOperation CreateProxyOperation(
        string endpointName, 
        string method, 
        string targetUrl,
        List<string> allowedEnvironments,
        int operationId,
        EndpointDefinition? definition = null)
    {
        var operation = new OpenApiOperation
        {
            Tags = new List<OpenApiTag> { new() { Name = endpointName } }, // Assign unique tag based on endpoint name
            Summary = definition != null ? GetOperationSummary(method, endpointName, definition) : $"{method} {endpointName}",
            Description = definition != null ? GetOperationDescription(method, endpointName, definition) : $"Proxy {method} request to {targetUrl}",
            OperationId = $"op_{operationId}",
            Parameters = new List<OpenApiParameter>
            {
                // Environment parameter
                new()
                {
                    Name = "env",
                    In = ParameterLocation.Path,
                    Required = true,
                    Schema = new OpenApiSchema
                    {
                        Type = "string",
                        Enum = allowedEnvironments.Select(e => new OpenApiString(e)).Cast<IOpenApiAny>().ToList()
                    },
                    Description = "Target environment"
                }
            }
        };
        
        // Add method-specific parameters and request body
        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase))
        {
            // Add query parameters for GET
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "$filter",
                In = ParameterLocation.Query,
                Required = false,
                Schema = new OpenApiSchema { Type = "string" },
                Description = "Filter expression"
            });
            
            // Add default $top=10 to the parameters for Proxy GET requests in Swagger
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "$top",
                In = ParameterLocation.Query,
                Required = false,
                Schema = new OpenApiSchema
                {
                    Type = "integer",
                    Default = new OpenApiInteger(10)
                },
                Description = "Maximum number of records to return (default is 10)"
            });
        }
        else if (method.Equals("POST", StringComparison.OrdinalIgnoreCase) || 
                 method.Equals("PUT", StringComparison.OrdinalIgnoreCase) ||
                 method.Equals("PATCH", StringComparison.OrdinalIgnoreCase) ||
                 method.Equals("MERGE", StringComparison.OrdinalIgnoreCase))
        {
            // Add request body for methods that support it
            operation.RequestBody = new OpenApiRequestBody
            {
                Description = "Request payload",
                Required = true,
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = "object"
                        }
                    },
                    ["text/xml"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema { Type = "string" }
                    },
                    ["application/soap+xml"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema { Type = "string" }
                    },
                    ["application/xml"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema { Type = "string" }
                    },
                    ["text/plain"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema { Type = "string" }
                    }
                }
            };
        }
        
        // Add standard responses
        operation.Responses = new OpenApiResponses
        {
            ["200"] = new OpenApiResponse { Description = "Successful response" },
            ["400"] = new OpenApiResponse { Description = "Bad Request - Invalid request" },
            ["401"] = new OpenApiResponse 
            { 
                Description = "Unauthorized - Missing or invalid authentication token",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, OpenApiSchema>
                            {
                                ["error"] = new OpenApiSchema { Type = "string" }
                            }
                        }
                    }
                }
            },
            ["403"] = new OpenApiResponse { Description = "Forbidden - Insufficient permissions" },
            ["404"] = new OpenApiResponse { Description = "Not Found - Resource not found" },
            ["500"] = new OpenApiResponse 
            { 
                Description = "Internal Server Error",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, OpenApiSchema>
                            {
                                ["error"] = new OpenApiSchema { Type = "string" },
                                ["message"] = new OpenApiSchema { Type = "string" }
                            }
                        }
                    }
                }
            }
        };
        
        return operation;
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
    
    private OperationType? GetOperationType(string method)
    {
        return method.ToUpperInvariant() switch
        {
            "GET" => OperationType.Get,
            "POST" => OperationType.Post,
            "PUT" => OperationType.Put,
            "DELETE" => OperationType.Delete,
            "PATCH" => OperationType.Patch,
            "OPTIONS" => OperationType.Options,
            "HEAD" => OperationType.Head,
            "MERGE" => OperationType.Patch, // Map MERGE to PATCH as they're semantically similar
            _ => null
        };
    }
    
    private void AddOperationToPath(OpenApiPathItem pathItem, string method, OpenApiOperation operation)
    {
        var operationType = GetOperationType(method);
        if (operationType.HasValue)
        {
            pathItem.Operations[operationType.Value] = operation;
        }
    }
    
    /// <summary>
    /// Gets the operation summary, using custom summary if available or default format
    /// </summary>
    private string GetOperationSummary(string method, string endpointName, EndpointDefinition definition)
    {
        // Check if there's a custom summary for this method in MethodDescriptions
        if (definition.Documentation?.MethodDescriptions?.TryGetValue(method.ToUpper(), out var customSummary) == true 
            && !string.IsNullOrWhiteSpace(customSummary))
        {
            return customSummary;
        }
        
        // Return default format
        return $"{method} {endpointName}";
    }
    
    /// <summary>
    /// Gets the operation description, using custom description if available or default format
    /// </summary>
    private string GetOperationDescription(string method, string endpointName, EndpointDefinition definition)
    {
        // Check if there's a custom detailed description for this method in MethodDocumentation
        if (definition.Documentation?.MethodDocumentation?.TryGetValue(method.ToUpper(), out var customDocumentation) == true 
            && !string.IsNullOrWhiteSpace(customDocumentation))
        {
            return customDocumentation;
        }
        
        // Return default description based on endpoint type
        if (definition.IsSql)
        {
            return $"{method} operation for entity {definition.DatabaseObjectName}";
        }
        else if (definition.Type == EndpointType.Files)
        {
            return method.ToUpper() switch
            {
                "GET" => $"Download content from the {endpointName} storage endpoint",
                "POST" => $"Upload content to the {endpointName} storage endpoint",
                _ => $"{method} operation for storage endpoint {endpointName}"
            };
        }
        else
        {
            return $"Proxies {method} requests to an internal webservice.";
        }
    }
    
    /// <summary>
    /// Gets the operation description for static endpoints, using custom description if available or default format
    /// </summary>
    private string GetStaticOperationDescription(string method, string endpointName, EndpointDefinition definition, string contentType)
    {
        // Check if there's a custom description for this method
        if (definition.Documentation?.MethodDocumentation?.TryGetValue(method.ToUpper(), out var customDescription) == true 
            && !string.IsNullOrWhiteSpace(customDescription))
        {
            return customDescription;
        }
        
        // Return default description for static endpoints
        return $"Returns {contentType} content from the {endpointName} endpoint.";
    }
    
    /// <summary>
    /// Adds all collected tags with descriptions to the OpenAPI document
    /// </summary>
    private void AddTagsToDocument(OpenApiDocument swaggerDoc, Dictionary<string, string> documentTags)
    {
        // Initialize tags collection if it doesn't exist
        swaggerDoc.Tags ??= new List<OpenApiTag>();
        
        // Add each tag with its description (sorting will be handled by AlphabeticalEndpointSorter)
        foreach (var tagEntry in documentTags)
        {
            var existingTag = swaggerDoc.Tags.FirstOrDefault(t => t.Name.Equals(tagEntry.Key, StringComparison.OrdinalIgnoreCase));
            if (existingTag == null)
            {
                swaggerDoc.Tags.Add(new OpenApiTag
                {
                    Name = tagEntry.Key,
                    Description = tagEntry.Value
                });
            }
            else
            {
                // Update existing tag description if it's empty
                if (string.IsNullOrWhiteSpace(existingTag.Description))
                {
                    existingTag.Description = tagEntry.Value;
                }
            }
        }
        
        // Add Webhook tag if webhooks exist (load description from entity.json)
        if (swaggerDoc.Paths.Any(p => p.Key.Contains("/webhook/")))
        {
            var webhookTag = swaggerDoc.Tags.FirstOrDefault(t => t.Name.Equals("Webhook", StringComparison.OrdinalIgnoreCase));
            if (webhookTag == null)
            {
                var webhookDocumentation = LoadWebhookDocumentation();
                swaggerDoc.Tags.Add(new OpenApiTag
                {
                    Name = "Webhook",
                    Description = webhookDocumentation?.TagDescription ?? "Endpoints for receiving and processing external webhook events"
                });
            }
        }
        
        // Sort all tags alphabetically
        swaggerDoc.Tags = swaggerDoc.Tags.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
    
    /// <summary>
    /// Load webhook documentation from entity.json file
    /// </summary>
    private Documentation? LoadWebhookDocumentation()
    {
        try
        {
            string webhookEntityPath = Path.Combine(Directory.GetCurrentDirectory(), "endpoints", "Webhooks", "entity.json");
            if (!File.Exists(webhookEntityPath))
            {
                _logger.LogWarning("⚠️ Webhook entity.json not found at: {Path}", webhookEntityPath);
                return null;
            }

            string json = File.ReadAllText(webhookEntityPath);
            var webhookEntity = JsonSerializer.Deserialize<WebhookEntity>(json);
            return webhookEntity?.Documentation;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error loading webhook documentation");
            return null;
        }
    }

    // Helper classes for deserializing entity.json
    private class WebhookEntity
    {
        public Documentation? Documentation { get; set; }
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