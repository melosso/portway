using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using Serilog;

namespace PortwayApi.Classes;

public partial class DynamicEndpointDocumentFilter
{
    /// <summary>Collects file endpoint tags for documentation (operations are handled by EndpointController)</summary>
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

    /// <summary>Adds static endpoints to the OpenAPI document</summary>
    private void AddStaticEndpoints(OpenApiDocument document, ref int operationIdCounter, Dictionary<string, string> documentTags)
    {
        var staticEndpoints = EndpointHandler.GetStaticEndpoints();

        // Sort endpoints by endpoint name to ensure alphabetical order in documentation
        var sortedStaticEndpoints = staticEndpoints
            .OrderBy(ep => ep.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var endpoint in sortedStaticEndpoints)
        {
            string endpointName = endpoint.Key;
            var definition = endpoint.Value;

            // Skip private endpoints
            if (definition.IsPrivate)
                continue;

            // Get effective environments for this endpoint (endpoint-specific or global fallback)
            var effectiveEnvironments = GetEffectiveEnvironments(definition);

            // Collect tag description using the DocumentationTag for proper namespace grouping
            string documentationTag = definition.DocumentationTag;
            if (!string.IsNullOrWhiteSpace(definition.Documentation?.TagDescription))
            {
                documentTags[documentationTag] = definition.Documentation.TagDescription;
            }

            // Get content type and filtering capability
            var contentType = definition.Properties?.GetValueOrDefault("ContentType", "text/plain")?.ToString() ?? "text/plain";
            var enableFiltering = (bool)(definition.Properties?.GetValueOrDefault("EnableFiltering", false) ?? false);

            // Create single OpenAPI path with environment parameter (use FullPath to include namespace if present)
            string path = $"/api/{{env}}/{definition.FullPath}";

            if (!document.Paths.ContainsKey(path))
            {
                document.Paths[path] = new OpenApiPathItem { Operations = new Dictionary<HttpMethod, OpenApiOperation>() };
            }

            // Add GET operation
            var getOperation = new OpenApiOperation
            {
                OperationId = $"get{endpointName}Static{operationIdCounter++}",
                Summary = definition.Documentation?.MethodDescriptions?.GetValueOrDefault("GET") ?? $"Get content from {endpointName}",
                Description = GetStaticOperationDescription("GET", endpointName, definition, contentType),
                Tags = new HashSet<OpenApiTagReference> { new OpenApiTagReference(documentationTag) },
                Parameters = new List<IOpenApiParameter>
                {
                    // Environment parameter
                    new OpenApiParameter
                    {
                        Name = "env",
                        In = ParameterLocation.Path,
                        Required = true,
                        Schema = new OpenApiSchema
                        {
                            Type = JsonSchemaType.String,
                            Enum = effectiveEnvironments.Select(e => (JsonNode?)JsonValue.Create(e)).Cast<JsonNode>().ToList()
                        },
                        Description = "Environment identifier"
                    }
                },
                Responses = new OpenApiResponses
                {
                    ["200"] = new OpenApiResponse
                    {
                        Description = "Successful response",
                        Content = new Dictionary<string, IOpenApiMediaType>
                        {
                            [contentType] = new OpenApiMediaType
                            {
                                Schema = contentType.Contains("json")
                                    ? new OpenApiSchema { Type = JsonSchemaType.Object, Description = "JSON content" }
                                    : new OpenApiSchema { Type = JsonSchemaType.String, Description = "Content" },
                                Examples = contentType.Contains("json")
                                    ? new Dictionary<string, IOpenApiExample>
                                    {
                                        ["sample_data"] = new OpenApiExample
                                        {
                                            Summary = "Sample JSON data",
                                            Value = new JsonArray
                                            {
                                                new JsonObject()
                                            }
                                        }
                                    }
                                    : new Dictionary<string, IOpenApiExample>
                                    {
                                        ["sample_text"] = new OpenApiExample
                                        {
                                            Summary = "Sample text content",
                                            Value = JsonValue.Create("Hello world")
                                        }
                                    }
                            }
                        }
                    },
                    ["304"] = new OpenApiResponse
                    {
                        Description = "Not Modified - Content has not changed since last request",
                        Headers = new Dictionary<string, IOpenApiHeader>
                        {
                            ["Last-Modified"] = new OpenApiHeader
                            {
                                Description = "Date when the content was last modified",
                                Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "date-time" }
                            },
                            ["ETag"] = new OpenApiHeader
                            {
                                Description = "Entity tag for the current version of the content",
                                Schema = new OpenApiSchema { Type = JsonSchemaType.String }
                            }
                        }
                    },
                    ["401"] = new OpenApiResponse
                    {
                        Description = "Unauthorized - Missing or invalid authentication token",
                        Content = new Dictionary<string, IOpenApiMediaType>
                        {
                            ["application/json"] = new OpenApiMediaType
                            {
                                Schema = new OpenApiSchema
                                {
                                    Type = JsonSchemaType.Object,
                                    Properties = new Dictionary<string, IOpenApiSchema>
                                    {
                                        ["error"] = new OpenApiSchema { Type = JsonSchemaType.String }
                                    }
                                },
                                Example = new JsonObject
                                {
                                    ["error"] = JsonValue.Create("Unauthorized access. Valid Bearer token required."),
                                    ["success"] = JsonValue.Create(false)
                                }
                            }
                        }
                    },
                    ["403"] = new OpenApiResponse
                    {
                        Description = "Forbidden - Insufficient permissions",
                        Content = new Dictionary<string, IOpenApiMediaType>
                        {
                            ["application/json"] = new OpenApiMediaType
                            {
                                Schema = new OpenApiSchema
                                {
                                    Type = JsonSchemaType.Object,
                                    Properties = new Dictionary<string, IOpenApiSchema>
                                    {
                                        ["error"] = new OpenApiSchema { Type = JsonSchemaType.String }
                                    }
                                },
                                Example = new JsonObject
                                {
                                    ["error"] = JsonValue.Create("Environment 'production' is not allowed for this endpoint"),
                                    ["success"] = JsonValue.Create(false)
                                }
                            }
                        }
                    },
                    ["404"] = new OpenApiResponse
                    {
                        Description = "Not Found - Resource not found",
                        Content = new Dictionary<string, IOpenApiMediaType>
                        {
                            ["application/json"] = new OpenApiMediaType
                            {
                                Schema = new OpenApiSchema
                                {
                                    Type = JsonSchemaType.Object,
                                    Properties = new Dictionary<string, IOpenApiSchema>
                                    {
                                        ["error"] = new OpenApiSchema { Type = JsonSchemaType.String }
                                    }
                                },
                                Examples = new Dictionary<string, IOpenApiExample>
                                {
                                    ["endpoint_not_found"] = new OpenApiExample
                                    {
                                        Summary = "Endpoint not found",
                                        Value = new JsonObject
                                        {
                                            ["error"] = JsonValue.Create($"Endpoint '{endpointName}' not found")
                                        }
                                    },
                                    ["content_not_found"] = new OpenApiExample
                                    {
                                        Summary = "Content not found",
                                        Value = new JsonObject
                                        {
                                            ["error"] = JsonValue.Create("Content not found")
                                        }
                                    }
                                }
                            }
                        }
                    },
                    ["406"] = new OpenApiResponse
                    {
                        Description = "Not Acceptable - Content negotiation failed",
                        Content = new Dictionary<string, IOpenApiMediaType>
                        {
                            ["application/json"] = new OpenApiMediaType
                            {
                                Schema = new OpenApiSchema
                                {
                                    Type = JsonSchemaType.Object,
                                    Properties = new Dictionary<string, IOpenApiSchema>
                                    {
                                        ["error"] = new OpenApiSchema { Type = JsonSchemaType.String },
                                        ["detail"] = new OpenApiSchema { Type = JsonSchemaType.String },
                                        ["availableContentType"] = new OpenApiSchema { Type = JsonSchemaType.String }
                                    }
                                },
                                Example = new JsonObject
                                {
                                    ["error"] = JsonValue.Create("Not Acceptable"),
                                    ["detail"] = JsonValue.Create($"Endpoint provides '{contentType}' but client accepts 'text/html'"),
                                    ["availableContentType"] = JsonValue.Create(contentType)
                                }
                            }
                        }
                    },
                    ["500"] = new OpenApiResponse
                    {
                        Description = "Internal Server Error",
                        Content = new Dictionary<string, IOpenApiMediaType>
                        {
                            ["application/json"] = new OpenApiMediaType
                            {
                                Schema = new OpenApiSchema
                                {
                                    Type = JsonSchemaType.Object,
                                    Properties = new Dictionary<string, IOpenApiSchema>
                                    {
                                        ["type"] = new OpenApiSchema { Type = JsonSchemaType.String },
                                        ["title"] = new OpenApiSchema { Type = JsonSchemaType.String },
                                        ["status"] = new OpenApiSchema { Type = JsonSchemaType.Integer },
                                        ["detail"] = new OpenApiSchema { Type = JsonSchemaType.String }
                                    }
                                },
                                Example = new JsonObject
                                {
                                    ["type"] = JsonValue.Create("https://tools.ietf.org/html/rfc7231#section-6.6.1"),
                                    ["title"] = JsonValue.Create("Error"),
                                    ["status"] = JsonValue.Create(500),
                                    ["detail"] = JsonValue.Create("Error processing. Please check the logs for more details.")
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
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                    Description = "Select specific fields (OData $select)"
                });

                getOperation.Parameters.Add(new OpenApiParameter
                {
                    Name = "$filter",
                    In = ParameterLocation.Query,
                    Required = false,
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                    Description = "Filter results (OData $filter)"
                });

                getOperation.Parameters.Add(new OpenApiParameter
                {
                    Name = "$orderby",
                    In = ParameterLocation.Query,
                    Required = false,
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                    Description = "Sort results (OData $orderby)"
                });

                getOperation.Parameters.Add(new OpenApiParameter
                {
                    Name = "$top",
                    In = ParameterLocation.Query,
                    Required = false,
                    Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Default = JsonValue.Create(10) },
                    Description = "Maximum number of results (OData $top)"
                });

                getOperation.Parameters.Add(new OpenApiParameter
                {
                    Name = "$skip",
                    In = ParameterLocation.Query,
                    Required = false,
                    Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Default = JsonValue.Create(0) },
                    Description = "Number of results to skip (OData $skip)"
                });
            }

            // Standardize static error responses onto the shared schema (validated matrix; 406 = content negotiation)
            getOperation.Responses ??= new OpenApiResponses();
            foreach (var __c in new[] { "400", "401", "403", "404", "405", "406", "409", "422", "500" })
                getOperation.Responses.Remove(__c);
            StandardResponses.AddErrors(getOperation, 400, 401, 403, 404, 406, 500);

            document.Paths[path].Operations![HttpMethod.Get] = getOperation;
        }
    }
}
