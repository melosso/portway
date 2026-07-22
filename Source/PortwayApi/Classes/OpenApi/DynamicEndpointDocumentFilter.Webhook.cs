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

    private void AddWebhookEndpoints(OpenApiDocument document, ref int operationIdCounter)
    {
        var webhookEndpoints = EndpointHandler.GetSqlWebhookEndpoints();
        if (webhookEndpoints == null || webhookEndpoints.Count == 0)
        {
            return; // Skip adding webhook endpoints to docs if none exist
        }

        // Emit one namespaced POST path per webhook endpoint: /api/{env}/{namespace}/{name}/{webhookId}
        foreach (var webhook in webhookEndpoints)
        {
        var definition = webhook.Value;
        string path = $"/api/{{env}}/{definition.FullPath}/{{webhookId}}";

        // Effective environments and documentation are resolved per endpoint
        var effectiveEnvironments = GetEffectiveEnvironments(definition);
        var webhookDocumentation = definition.Documentation ?? LoadWebhookDocumentation();
        var webhookTag = definition.DocumentationTag;

        // Register this webhook's tag description; namespaced webhooks carry it in their own Documentation block
        if (!string.IsNullOrWhiteSpace(webhookDocumentation?.TagDescription))
        {
            document.Tags ??= new HashSet<OpenApiTag>();
            var existingTag = document.Tags.FirstOrDefault(t => string.Equals(t.Name, webhookTag, StringComparison.OrdinalIgnoreCase));
            if (existingTag == null)
            {
                document.Tags.Add(new OpenApiTag { Name = webhookTag, Description = webhookDocumentation.TagDescription });
            }
            else if (string.IsNullOrWhiteSpace(existingTag.Description))
            {
                existingTag.Description = webhookDocumentation.TagDescription;
            }
        }

        // Create path item if it doesn't exist
        if (!document.Paths.ContainsKey(path))
        {
            document.Paths[path] = new OpenApiPathItem { Operations = new Dictionary<HttpMethod, OpenApiOperation>() };
        }

        // Create webhook POST operation
        var webhookOperation = new OpenApiOperation
        {
            Tags = new HashSet<OpenApiTagReference> { new(webhookTag) },
            Summary = webhookDocumentation?.MethodDescriptions?.GetValueOrDefault("POST") ?? "Process incoming request",
            Description = webhookDocumentation?.MethodDocumentation?.GetValueOrDefault("POST") ?? "Receives and processes a request payload",
            OperationId = $"op_{operationIdCounter++}",
            Parameters = new List<IOpenApiParameter>
            {
                new OpenApiParameter()
                {
                    Name = "env",
                    In = ParameterLocation.Path,
                    Required = true,
                    Schema = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String,
                        Enum = effectiveEnvironments.Select(e => (JsonNode?)JsonValue.Create(e)).Cast<JsonNode>().ToList()
                    },
                    Description = "Target environment"
                },
                new OpenApiParameter()
                {
                    Name = "webhookId",
                    In = ParameterLocation.Path,
                    Required = true,
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                    Description = "Webhook identifier"
                }
            },
            RequestBody = new OpenApiRequestBody
            {
                Description = "Webhook payload (any valid JSON)",
                Required = true,
                Content = new Dictionary<string, IOpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = JsonSchemaType.Object
                        }
                    }
                }
            },
            Responses = new OpenApiResponses
            {
                ["201"] = new OpenApiResponse
                {
                    Description = "Created - Resource successfully created",
                    Headers = new Dictionary<string, IOpenApiHeader>
                    {
                        ["Location"] = new OpenApiHeader
                        {
                            Description = "URL of the newly created resource",
                            Schema = new OpenApiSchema { Type = JsonSchemaType.String }
                        }
                    },
                    Content = new Dictionary<string, IOpenApiMediaType>
                    {
                        ["application/json"] = new OpenApiMediaType
                        {
                            Schema = new OpenApiSchema
                            {
                                Type = JsonSchemaType.Object,
                                Properties = new Dictionary<string, IOpenApiSchema>
                                {
                                    ["success"] = new OpenApiSchema { Type = JsonSchemaType.Boolean },
                                    ["message"] = new OpenApiSchema { Type = JsonSchemaType.String, Example = JsonValue.Create("Request processed successfully.") },
                                    ["result"]  = new OpenApiSchema { Type = JsonSchemaType.Object | JsonSchemaType.Null },
                                    ["id"]      = new OpenApiSchema { Type = JsonSchemaType.Integer, Example = JsonValue.Create(12345) }
                                }
                            },
                            Example = new JsonObject
                            {
                                ["success"] = JsonValue.Create(true),
                                ["message"] = JsonValue.Create("Webhook processed successfully."),
                                ["result"]  = null,
                                ["id"]      = JsonValue.Create(12345)
                            }
                        }
                    }
                },
                ["400"] = new OpenApiResponse
                {
                    Description = "Bad Request - Invalid request",
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
                                    ["success"] = new OpenApiSchema { Type = JsonSchemaType.Boolean, Example = JsonValue.Create(false) }
                                }
                            },
                            Example = new JsonObject
                            {
                                ["error"] = JsonValue.Create("Environment is not configured properly."),
                                ["success"] = JsonValue.Create(false)
                            }
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
                                ["error"] = JsonValue.Create("Unauthorized access. Valid authentication token required."),
                                ["success"] = JsonValue.Create(false)
                            }
                        }
                    }
                },
                ["403"] = new OpenApiResponse
                {
                    Description = "Forbidden - Token valid but insufficient permissions",
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
                                ["error"] = JsonValue.Create("Environment 'production' is not allowed."),
                                ["success"] = JsonValue.Create(false)
                            }
                        }
                    }
                },
                ["404"] = new OpenApiResponse
                {
                    Description = "Not Found - Resource not found or not configured",
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
                                    ["success"] = new OpenApiSchema { Type = JsonSchemaType.Boolean, Example = JsonValue.Create(false) }
                                }
                            },
                            Example = new JsonObject
                            {
                                ["error"] = JsonValue.Create("Webhook ID 'unknown_webhook' is not configured."),
                                ["success"] = JsonValue.Create(false)
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

        // Standardize webhook error responses onto the shared schema (validated matrix)
        foreach (var c in new[] { "400", "401", "403", "404", "405", "409", "422", "500" })
            webhookOperation.Responses.Remove(c);
        StandardResponses.AddErrors(webhookOperation, 400, 401, 403, 404, 500);

        document.Paths[path].Operations![HttpMethod.Post] = webhookOperation;
        }
    }

}
