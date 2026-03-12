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

public class DynamicEndpointDocumentFilter : IOpenApiDocumentTransformer
{
    private readonly ILogger<DynamicEndpointDocumentFilter> _logger;

    public DynamicEndpointDocumentFilter(ILogger<DynamicEndpointDocumentFilter> logger)
    {
        _logger = logger;
    }

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        _logger.LogDebug("DynamicEndpointDocumentFilter called");

        try {
            // Remove any controller-discovered paths that we'll be replacing
            RemoveConflictingPaths(document);

            // Generate unique operation IDs
            int operationIdCounter = 1;

            // Collect all tags with descriptions for the document
            var documentTags = new Dictionary<string, string>();

            // Add documentation for each endpoint type
            AddSqlEndpoints(document, ref operationIdCounter, documentTags);
            AddProxyEndpoints(document, ref operationIdCounter, documentTags);
            AddWebhookEndpoints(document, ref operationIdCounter);
            AddStaticEndpoints(document, ref operationIdCounter, documentTags);

            // Collect file endpoint tags (but don't create operations since they're handled by EndpointController)
            CollectFileEndpointTags(documentTags);

            // Add all collected tags to the document with descriptions
            AddTagsToDocument(document, documentTags);


            // Ensure application/json is added automatically to all operations with request bodies
            foreach (var path in document.Paths)
            {
                if (path.Value.Operations == null) continue;
                foreach (var operation in path.Value.Operations)
                {
                    if (operation.Value.RequestBody?.Content != null && !operation.Value.RequestBody.Content.ContainsKey("application/json"))
                    {
                        operation.Value.RequestBody.Content["application/json"] = new OpenApiMediaType
                        {
                            Schema = new OpenApiSchema { Type = JsonSchemaType.Object }
                        };
                    }
                }
            }

            return Task.CompletedTask;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error applying document filter");
            return Task.CompletedTask;
        }
    }

    private void RemoveConflictingPaths(OpenApiDocument document)
    {
        var pathsToRemove = document.Paths
            .Where(p => p.Key.Contains("{catchall}") || p.Key.Contains("api/{env}"))
            .Select(p => p.Key)
            .ToList();

        foreach (var path in pathsToRemove)
        {
            document.Paths.Remove(path);
        }
    }

    private void AddSqlEndpoints(OpenApiDocument document, ref int operationIdCounter, Dictionary<string, string> documentTags)
    {
        // Get SQL endpoints
        var sqlEndpoints = EndpointHandler.GetSqlEndpoints();

        // Sort endpoints by DocumentationTag to ensure alphabetical order in documentation
        var sortedSqlEndpoints = sqlEndpoints
            .OrderBy(ep => ep.Value.DocumentationTag, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var endpoint in sortedSqlEndpoints)
        {
            string endpointName = endpoint.Key;
            var definition = endpoint.Value;

            // Get effective environments for this endpoint (endpoint-specific or global fallback)
            var effectiveEnvironments = GetEffectiveEnvironments(definition);

            // Collect tag description if provided
            if (!string.IsNullOrWhiteSpace(definition.Documentation?.TagDescription))
            {
                documentTags[definition.DocumentationTag] = definition.Documentation.TagDescription;
            }

            // Path template for this endpoint (use FullPath to include namespace if present)
            string path = $"/api/{{env}}/{definition.FullPath}";

            // Create path item if it doesn't exist
            if (!document.Paths.ContainsKey(path))
            {
                document.Paths[path] = new OpenApiPathItem { Operations = new Dictionary<HttpMethod, OpenApiOperation>() };
            }

            // Add operations based on allowed methods
            var methods = definition.Methods;

            // Ensure GET is always included (even if not specified)
            if (!methods.Contains("GET", StringComparer.OrdinalIgnoreCase))
            {
                methods.Add("GET");
            }

            // Add each allowed operation, but skip DELETE for the base path
            foreach (var method in methods)
            {
                if (method.Equals("DELETE", StringComparison.OrdinalIgnoreCase))
                    continue;
                var opType = GetOperationType(method);

                if (opType == null)
                    continue;

                var operation = CreateSqlOperation(
                    endpointName,
                    method,
                    definition,
                    effectiveEnvironments,
                    operationIdCounter++);

                if (opType != null)
                {
                    document.Paths[path].Operations![opType] = operation;
                }
            }

            // Add specific delete endpoint with OData-style ID in path
            if (methods.Contains("DELETE", StringComparer.OrdinalIgnoreCase))
            {
                // Use OData-style path: /api/{env}/{endpointName}({id})
                var deletePath = $"/api/{{env}}/{definition.FullPath}({{id}})";

                // Create path item if it doesn't exist
                if (!document.Paths.ContainsKey(deletePath))
                {
                    document.Paths[deletePath] = new OpenApiPathItem { Operations = new Dictionary<HttpMethod, OpenApiOperation>() };
                }

                var deleteOperation = CreateSqlDeleteOperation(
                    endpointName,
                    definition,
                    effectiveEnvironments,
                    operationIdCounter++);

                // Remove the query parameter for id, and add a path parameter instead
                deleteOperation.Parameters = (deleteOperation.Parameters ?? new List<IOpenApiParameter>())
                    .Where(p => p.Name != "id")
                    .ToList();

                deleteOperation.Parameters.Add(new OpenApiParameter {
                    Name = "id",
                    In = ParameterLocation.Path,
                    Required = true,
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                    Description = "ID of the record to delete (OData-style: /endpointName(id))"
                });

                document.Paths[deletePath].Operations![HttpMethod.Delete] = deleteOperation;
            }
        }
    }

    private void AddProxyDeleteOperation(
        OpenApiDocument document,
        string endpointName,
        EndpointDefinition definition,
        ref int operationIdCounter)
    {
        // Get effective environments for this endpoint (endpoint-specific or global fallback)
        var effectiveEnvironments = GetEffectiveEnvironments(definition);

        // Determine delete pattern (load from endpoint definition)
        var deletePattern = GetDeletePatternForDocs(definition);

        // Create path based on delete pattern
        string deletePath = deletePattern.Style == "PathParameter" ||
                            deletePattern.Style == "ODataGuid" ||
                            deletePattern.Style == "ODataKey"
            ? $"/api/{{env}}/{endpointName}/{{id}}"
            : $"/api/{{env}}/{endpointName}";

        if (!document.Paths.ContainsKey(deletePath))
        {
            document.Paths[deletePath] = new OpenApiPathItem { Operations = new Dictionary<HttpMethod, OpenApiOperation>() };
        }

        var operation = new OpenApiOperation
        {
            Tags = new HashSet<OpenApiTagReference> { new OpenApiTagReference(definition.DocumentationTag) },
            Summary = GetOperationSummary("DELETE", definition.DisplayName ?? definition.EndpointName, definition),
            Description = GetOperationDescription("DELETE", definition.DisplayName ?? definition.EndpointName, definition),
            OperationId = $"delete_{definition.FullPath}".Replace("/", "_"),
            Parameters = new List<IOpenApiParameter>()
        };

        // Environment parameter
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "env",
            In = ParameterLocation.Path,
            Required = true,
            Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Enum = effectiveEnvironments.Select(e => (JsonNode?)JsonValue.Create(e)).Cast<JsonNode>().ToList()
            },
            Description = $"Target environment. Allowed values: {string.Join(", ", effectiveEnvironments)}"
        });

        // ID parameter based on pattern
        if (deletePattern.Style == "PathParameter" ||
            deletePattern.Style == "ODataGuid" ||
            deletePattern.Style == "ODataKey")
        {
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "id",
                In = ParameterLocation.Path,
                Required = true,
                Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                Description = deletePattern.Description ?? "Resource ID to delete"
            });
        }
        else if (deletePattern.Style == "QueryParameter")
        {
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = deletePattern.Parameter ?? "id",
                In = ParameterLocation.Query,
                Required = true,
                Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                Description = deletePattern.Description ?? "Resource ID to delete"
            });
        }

        // Add responses
        operation.Responses = new OpenApiResponses
        {
            ["200"] = new OpenApiResponse { Description = "Successfully deleted" },
            ["204"] = new OpenApiResponse { Description = "Successfully deleted (no content)" },
            ["400"] = new OpenApiResponse { Description = "Bad request" },
            ["401"] = new OpenApiResponse { Description = "Unauthorized" },
            ["404"] = new OpenApiResponse { Description = "Resource not found" },
            ["500"] = new OpenApiResponse
            {
                Description = "Internal Server Error",
                Content = new Dictionary<string, OpenApiMediaType>
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
        };

        document.Paths[deletePath].Operations![HttpMethod.Delete] = operation;
        operationIdCounter++;
    }

    private DeletePattern GetDeletePatternForDocs(EndpointDefinition definition)
    {
        if (definition.DeletePatterns?.Any() == true)
        {
            return definition.DeletePatterns.First();
        }

        // Default fallback
        return new DeletePattern
        {
            Style = "PathParameter",
            Description = "Delete resource by ID in path"
        };
    }

    private void AddProxyEndpoints(OpenApiDocument document, ref int operationIdCounter, Dictionary<string, string> documentTags)
    {
        var proxyEndpoints = EndpointHandler.GetProxyEndpoints();

        var sortedProxyEndpoints = proxyEndpoints
            .OrderBy(ep => ep.Value.DocumentationTag, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var endpoint in sortedProxyEndpoints)
        {
            string endpointName = endpoint.Key;
            var definition = endpoint.Value;

            if (definition.IsPrivate || definition.IsComposite)
                continue;

            // Get effective environments for this endpoint (endpoint-specific or global fallback)
            var effectiveEnvironments = GetEffectiveEnvironments(definition);

            if (!string.IsNullOrWhiteSpace(definition.Documentation?.TagDescription) &&
                !documentTags.ContainsKey(definition.DocumentationTag))
            {
                documentTags[definition.DocumentationTag] = definition.Documentation.TagDescription;
            }

            string path = $"/api/{{env}}/{endpointName}";

            if (!document.Paths.ContainsKey(path))
            {
                document.Paths[path] = new OpenApiPathItem { Operations = new Dictionary<HttpMethod, OpenApiOperation>() };
            }

            // Determine content type from CustomProperties or default to application/json
            var acceptContentType = definition.CustomProperties?.GetValueOrDefault("ContentType")?.ToString() ?? "application/json";

            // Add operations for each HTTP method
            foreach (var method in definition.Methods)
            {
                // Skip DELETE - handle it separately
                if (method.Equals("DELETE", StringComparison.OrdinalIgnoreCase))
                    continue;

                var operation = new OpenApiOperation
                {
                    Tags = new HashSet<OpenApiTagReference> { new OpenApiTagReference(definition.DocumentationTag) },
                    Summary = GetOperationSummary(method, definition.DisplayName ?? definition.EndpointName, definition),
                    Description = GetOperationDescription(method, definition.DisplayName ?? definition.EndpointName, definition),
                    OperationId = $"{method.ToLower()}_{definition.FullPath}".Replace(" ", "_").Replace("/", "_"),
                    Parameters = new List<IOpenApiParameter>()
                };

                // Add environment parameter
                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = "env",
                    In = ParameterLocation.Path,
                    Required = true,
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String, Enum = effectiveEnvironments.Select(e => (JsonNode?)JsonValue.Create(e)).Cast<JsonNode>().ToList() },
                    Description = $"Environment to target. Allowed values: {string.Join(", ", effectiveEnvironments)}"
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
                        Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                        Description = "Select specific fields (comma-separated list of property names)"
                    });

                    // Add $top parameter with default value
                    operation.Parameters.Add(new OpenApiParameter
                    {
                        Name = "$top",
                        In = ParameterLocation.Query,
                        Required = false,
                        Schema = new OpenApiSchema {
                            Type = JsonSchemaType.Integer,
                            Default = JsonValue.Create(10),
                            Minimum = "1",
                            Maximum = "1000"
                        },
                        Description = "Limit the number of results returned (default: 10, max: 1000)"
                    });

                    // Add $filter parameter
                    operation.Parameters.Add(new OpenApiParameter
                    {
                        Name = "$filter",
                        In = ParameterLocation.Query,
                        Required = false,
                        Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                        Description = "Filter the results based on a condition (e.g., Name eq 'Value')"
                    });
                }

                // Add request body for methods that support it
                if (method.Equals("POST", StringComparison.OrdinalIgnoreCase) ||
                    method.Equals("PUT", StringComparison.OrdinalIgnoreCase) ||
                    method.Equals("PATCH", StringComparison.OrdinalIgnoreCase) ||
                    method.Equals("MERGE", StringComparison.OrdinalIgnoreCase))
                {
                    var requestContent = new Dictionary<string, OpenApiMediaType>();

                    // If a specific ContentType is configured, use it as the primary content type
                    if (acceptContentType != "application/json")
                    {
                        requestContent[acceptContentType] = new OpenApiMediaType
                        {
                            Schema = GetSchemaForContentType(acceptContentType)
                        };
                    }

                    // Always include standard content types as alternatives
                    foreach (var ct in new[] { "application/json", "text/xml", "application/soap+xml", "application/xml", "text/plain" })
                    {
                        if (!requestContent.ContainsKey(ct))
                        {
                            requestContent[ct] = new OpenApiMediaType
                            {
                                Schema = GetSchemaForContentType(ct)
                            };
                        }
                    }

                    operation.RequestBody = new OpenApiRequestBody
                    {
                        Required = true,
                        Content = requestContent
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
                            [acceptContentType] = new OpenApiMediaType
                            {
                                Schema = GetSchemaForContentType(acceptContentType)
                            }
                        }
                    },
                    ["400"] = new OpenApiResponse { Description = "Bad Request - Invalid request" },
                    ["401"] = new OpenApiResponse { Description = "Unauthorized - Missing or invalid authentication token" },
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
                };

                // Add the operation to the path with the appropriate HTTP method
                AddOperationToPath(document.Paths[path], method, operation);
            }

            // Special handling for DELETE
            if (definition.Methods.Contains("DELETE", StringComparer.OrdinalIgnoreCase))
            {
                AddProxyDeleteOperation(document, endpointName, definition, ref operationIdCounter);
            }
        }
    }

    private void AddWebhookEndpoints(OpenApiDocument document, ref int operationIdCounter)
    {
        // Add webhook endpoint with correct path pattern
        string path = "/api/{env}/webhook/{webhookId}";

        var webhookEndpoints = EndpointHandler.GetSqlWebhookEndpoints();
        if (webhookEndpoints == null || webhookEndpoints.Count == 0)
        {
            return; // Skip adding webhook endpoints to docs if none exist
        }

        // Get effective environments from the first webhook endpoint (they should all have the same restrictions)
        var firstWebhook = webhookEndpoints.Values.FirstOrDefault();
        var effectiveEnvironments = GetEffectiveEnvironments(firstWebhook);

        // Load webhook documentation from entity.json
        var webhookDocumentation = LoadWebhookDocumentation();

        // Create path item if it doesn't exist
        if (!document.Paths.ContainsKey(path))
        {
            document.Paths[path] = new OpenApiPathItem { Operations = new Dictionary<HttpMethod, OpenApiOperation>() };
        }

        // Create webhook POST operation
        var webhookOperation = new OpenApiOperation
        {
            Tags = new HashSet<OpenApiTagReference> { new("Webhook") },
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
                Content = new Dictionary<string, OpenApiMediaType>
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
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new OpenApiMediaType
                        {
                            Schema = new OpenApiSchema
                            {
                                Type = JsonSchemaType.Object,
                                Properties = new Dictionary<string, IOpenApiSchema>
                                {
                                    ["message"] = new OpenApiSchema { Type = JsonSchemaType.String, Example = JsonValue.Create("Request processed successfully.") },
                                    ["id"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Example = JsonValue.Create(12345) }
                                }
                            },
                            Example = new JsonObject
                            {
                                ["message"] = JsonValue.Create("Webhook processed successfully."),
                                ["id"] = JsonValue.Create(12345)
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
                    Content = new Dictionary<string, OpenApiMediaType>
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
                    Content = new Dictionary<string, OpenApiMediaType>
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
                    Content = new Dictionary<string, OpenApiMediaType>
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
                    Content = new Dictionary<string, OpenApiMediaType>
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

        document.Paths[path].Operations![HttpMethod.Post] = webhookOperation;
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
                        Content = new Dictionary<string, OpenApiMediaType>
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
                        Content = new Dictionary<string, OpenApiMediaType>
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
                        Content = new Dictionary<string, OpenApiMediaType>
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
                        Content = new Dictionary<string, OpenApiMediaType>
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
                        Content = new Dictionary<string, OpenApiMediaType>
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
                        Content = new Dictionary<string, OpenApiMediaType>
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

            document.Paths[path].Operations![HttpMethod.Get] = getOperation;
        }
    }

    private OpenApiOperation CreateSqlOperation(
        string endpointName,
        string method,
        EndpointDefinition definition,
        List<string> effectiveEnvironments,
        int operationId)
    {
        // Determine Accept content type from CustomProperties or default to application/json
        var acceptContentType = definition.CustomProperties?.GetValueOrDefault("ContentType")?.ToString() ?? "application/json";

        var operation = new OpenApiOperation
        {
            Tags = new HashSet<OpenApiTagReference> { new(definition.DocumentationTag) }, // Use DocumentationTag for proper namespace grouping
            Summary = GetOperationSummary(method, endpointName, definition),
            Description = GetOperationDescription(method, endpointName, definition),
            OperationId = $"op_{operationId}",
            Parameters = new List<IOpenApiParameter>
            {
                // Environment parameter
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
                }
            }
        };

        // Add method-specific parameters and request body
        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase))
        {
            // Add GET-specific parameters
            foreach (var parameter in new List<IOpenApiParameter>
            {
                new OpenApiParameter()
                {
                    Name = "$select",
                    In = ParameterLocation.Query,
                    Required = false,
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                    Description = "Select specific fields (comma-separated list of property names)"
                },
                new OpenApiParameter()
                {
                    Name = "$filter",
                    In = ParameterLocation.Query,
                    Required = false,
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                    Description = "OData $filter expression"
                },
                new OpenApiParameter()
                {
                    Name = "$orderby",
                    In = ParameterLocation.Query,
                    Required = false,
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                    Description = "OData $orderby expression"
                },
                new OpenApiParameter()
                {
                    Name = "$top",
                    In = ParameterLocation.Query,
                    Required = false,
                    Schema = new OpenApiSchema {
                        Type = JsonSchemaType.Integer,
                        Default = JsonValue.Create(10)
                    },
                    Description = "Maximum number of records to return"
                },
                new OpenApiParameter()
                {
                    Name = "$skip",
                    In = ParameterLocation.Query,
                    Required = false,
                    Schema = new OpenApiSchema {
                        Type = JsonSchemaType.Integer,
                        Default = JsonValue.Create(0)
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
            // Add request body for POST and PUT, using configured ContentType as primary
            var requestContent = new Dictionary<string, OpenApiMediaType>
            {
                [acceptContentType] = new OpenApiMediaType
                {
                    Schema = GetSchemaForContentType(acceptContentType)
                }
            };

            // Always include application/json as an alternative if not already the primary
            if (acceptContentType != "application/json")
            {
                requestContent["application/json"] = new OpenApiMediaType
                {
                    Schema = new OpenApiSchema { Type = JsonSchemaType.Object }
                };
            }

            operation.RequestBody = new OpenApiRequestBody
            {
                Description = method.Equals("POST") ? "Data for new record" : "Data for updated record",
                Required = true,
                Content = requestContent
            };
        }

        // Add comprehensive responses with detailed schemas and examples
        operation.Responses = new OpenApiResponses();

        // For POST, use 201 Created with Location header
        if (method.Equals("POST", StringComparison.OrdinalIgnoreCase))
        {
            operation.Responses["201"] = new OpenApiResponse
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
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    [acceptContentType] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = JsonSchemaType.Object,
                            Properties = new Dictionary<string, IOpenApiSchema>
                            {
                                ["success"] = new OpenApiSchema { Type = JsonSchemaType.Boolean },
                                ["message"] = new OpenApiSchema { Type = JsonSchemaType.String },
                                ["result"] = new OpenApiSchema
                                {
                                    Type = JsonSchemaType.Array,
                                    Items = new OpenApiSchema { Type = JsonSchemaType.Object }
                                }
                            }
                        }
                    }
                }
            };
        }
        else
        {
            // For GET, PUT, MERGE, DELETE use 200 OK
            operation.Responses["200"] = new OpenApiResponse
            {
                Description = method switch
                {
                    "GET" => "Successful response",
                    "PUT" => "Successful response",
                    "MERGE" => "Successful response",
                    "DELETE" => "Successful response",
                    _ => "Successful response"
                },
                // Add pagination headers for GET responses
                Headers = method.Equals("GET", StringComparison.OrdinalIgnoreCase) ? new Dictionary<string, IOpenApiHeader>
                {
                    ["X-Total-Count"] = new OpenApiHeader
                    {
                        Description = "Total number of records available (when $count=true)",
                        Schema = new OpenApiSchema { Type = JsonSchemaType.Integer }
                    },
                    ["X-Returned-Count"] = new OpenApiHeader
                    {
                        Description = "Number of records returned in this response",
                        Schema = new OpenApiSchema { Type = JsonSchemaType.Integer }
                    },
                    ["X-Has-More-Results"] = new OpenApiHeader
                    {
                        Description = "Indicates if more results are available (true/false)",
                        Schema = new OpenApiSchema { Type = JsonSchemaType.Boolean }
                    },
                    ["Cache-Control"] = new OpenApiHeader
                    {
                        Description = "Caching directive (when caching is enabled for the endpoint)",
                        Schema = new OpenApiSchema { Type = JsonSchemaType.String }
                    }
                } : null,
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    [acceptContentType] = new OpenApiMediaType
                    {
                        Schema = method switch
                        {
                            "GET" => new OpenApiSchema
                            {
                                Type = JsonSchemaType.Object,
                                Properties = new Dictionary<string, IOpenApiSchema>
                                {
                                    ["Success"] = new OpenApiSchema { Type = JsonSchemaType.Boolean },
                                    ["Count"] = new OpenApiSchema { Type = JsonSchemaType.Integer },
                                    ["Value"] = new OpenApiSchema
                                    {
                                        Type = JsonSchemaType.Array,
                                        Items = new OpenApiSchema { Type = JsonSchemaType.Object }
                                    },
                                    ["NextLink"] = new OpenApiSchema { Type = JsonSchemaType.String }
                                }
                            },
                            "PUT" or "MERGE" => new OpenApiSchema
                            {
                                Type = JsonSchemaType.Object,
                                Properties = new Dictionary<string, IOpenApiSchema>
                                {
                                    ["success"] = new OpenApiSchema { Type = JsonSchemaType.Boolean },
                                    ["message"] = new OpenApiSchema { Type = JsonSchemaType.String },
                                    ["result"] = new OpenApiSchema
                                    {
                                        Type = JsonSchemaType.Array,
                                        Items = new OpenApiSchema { Type = JsonSchemaType.Object }
                                    }
                                }
                            },
                            _ => new OpenApiSchema { Type = JsonSchemaType.Object }
                        },
                        Examples = method switch
                        {
                            "GET" => new Dictionary<string, IOpenApiExample>
                            {
                                ["success"] = new OpenApiExample
                                {
                                    Summary = "Successful data retrieval",
                                    Value = new JsonObject
                                    {
                                        ["Success"] = JsonValue.Create(true),
                                        ["Count"] = JsonValue.Create(0),
                                        ["Value"] = new JsonArray(),
                                        ["NextLink"] = JsonValue.Create($"/api/{endpointName}?$$top=10&$$skip=0")
                                    }
                                }
                            },
                            "PUT" or "MERGE" => new Dictionary<string, IOpenApiExample>
                            {
                                ["success"] = new OpenApiExample
                                {
                                    Summary = "Successful update",
                                    Value = new JsonObject
                                    {
                                        ["success"] = JsonValue.Create(true),
                                        ["message"] = JsonValue.Create("Record updated successfully"),
                                        ["result"] = new JsonArray
                                        {
                                            new JsonObject
                                            {
                                                ["result"] = JsonValue.Create("array")
                                            }
                                        }
                                    }
                                }
                            },
                            _ => new Dictionary<string, IOpenApiExample>()
                        }
                    }
                }
            };
            operation.Responses["400"] = new OpenApiResponse
            {
                Description = "Bad Request - Invalid request",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = JsonSchemaType.Object,
                            Properties = new Dictionary<string, IOpenApiSchema>
                            {
                                ["error"] = new OpenApiSchema { Type = JsonSchemaType.String },
                                ["details"] = new OpenApiSchema { Type = JsonSchemaType.String }
                            }
                        },
                        Examples = new Dictionary<string, IOpenApiExample>
                        {
                            ["invalid_odata"] = new OpenApiExample
                            {
                                Summary = "Invalid OData syntax",
                                Value = new JsonObject
                                {
                                    ["success"] = JsonValue.Create(false),
                                    ["error"] = JsonValue.Create("Invalid OData filter syntax"),
                                    ["details"] = JsonValue.Create("Syntax error in $filter expression near 'invalidField'")

                                }
                            },
                            ["missing_required"] = new OpenApiExample
                            {
                                Summary = "Missing required field",
                                Value = new JsonObject
                                {
                                    ["success"] = JsonValue.Create(false),
                                    ["error"] = JsonValue.Create("Required field 'name' is missing"),
                                    ["details"] = JsonValue.Create("POST request must include 'name' field in request body")
                                }
                            }
                        }
                    }
                }
            };
            operation.Responses["401"] = new OpenApiResponse
            {
                Description = "Unauthorized - Missing or invalid authentication token",
                Content = new Dictionary<string, OpenApiMediaType>
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
            };
            operation.Responses["403"] = new OpenApiResponse
            {
                Description = "Forbidden - Insufficient permissions",
                Content = new Dictionary<string, OpenApiMediaType>
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
                            ["environment_restricted"] = new OpenApiExample
                            {
                                Summary = "Environment access denied",
                                Value = new JsonObject
                                {
                                    ["error"] = JsonValue.Create("Environment 'production' is not allowed for this endpoint"),
                                    ["success"] = JsonValue.Create(false)
                                }
                            },
                            ["method_not_allowed"] = new OpenApiExample
                            {
                                Summary = "Method not allowed",
                                Value = new JsonObject
                                {
                                    ["error"] = JsonValue.Create("DELETE method is not allowed for this endpoint")
                                }
                            }
                        }
                    }
                }
            };
            operation.Responses["404"] = new OpenApiResponse
            {
                Description = "Not Found - Resource not found",
                Content = new Dictionary<string, OpenApiMediaType>
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
                            ["record_not_found"] = new OpenApiExample
                            {
                                Summary = "Record not found",
                                Value = new JsonObject
                                {
                                    ["error"] = JsonValue.Create("Record with specified ID not found")
                                }
                            }
                        }
                    }
                }
            };
            operation.Responses["409"] = new OpenApiResponse
            {
                Description = "Conflict - Duplicate key violation or business rule conflict",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = JsonSchemaType.Object,
                            Properties = new Dictionary<string, IOpenApiSchema>
                            {
                                ["error"] = new OpenApiSchema { Type = JsonSchemaType.String },
                                ["conflictType"] = new OpenApiSchema { Type = JsonSchemaType.String },
                                ["details"] = new OpenApiSchema { Type = JsonSchemaType.String }
                            }
                        },
                        Example = new JsonObject
                        {
                            ["error"] = JsonValue.Create("Record with this identifier already exists"),
                            ["conflictType"] = JsonValue.Create("CONSTRAINT_VIOLATION"),
                            ["details"] = JsonValue.Create("Violation of the constraint 'UK_Name'")
                        }
                    }
                }
            };
            operation.Responses["422"] = new OpenApiResponse
            {
                Description = "Unprocessable Entity - Validation errors or business rule violations",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = JsonSchemaType.Object,
                            Properties = new Dictionary<string, IOpenApiSchema>
                            {
                                ["error"] = new OpenApiSchema { Type = JsonSchemaType.String },
                                ["validationErrors"] = new OpenApiSchema
                                {
                                    Type = JsonSchemaType.Array,
                                    Items = new OpenApiSchema
                                    {
                                        Type = JsonSchemaType.Object,
                                        Properties = new Dictionary<string, IOpenApiSchema>
                                        {
                                            ["field"] = new OpenApiSchema { Type = JsonSchemaType.String },
                                            ["message"] = new OpenApiSchema { Type = JsonSchemaType.String }
                                        }
                                    }
                                }
                            }
                        },
                        Example = new JsonObject
                        {
                            ["error"] = JsonValue.Create("Validation failed"),
                            ["validationErrors"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["field"] = JsonValue.Create("email"),
                                    ["message"] = JsonValue.Create("Invalid email format")
                                },
                                new JsonObject
                                {
                                    ["field"] = JsonValue.Create("age"),
                                    ["message"] = JsonValue.Create("Age must be between 18 and 120")
                                }
                            }
                        }
                    }
                }
            };
            operation.Responses["500"] = new OpenApiResponse
            {
                Description = "Internal Server Error",
                Content = new Dictionary<string, OpenApiMediaType>
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
                                ["detail"] = new OpenApiSchema { Type = JsonSchemaType.String },
                                ["traceId"] = new OpenApiSchema { Type = JsonSchemaType.String }
                            }
                        },
                        Example = new JsonObject
                        {
                            ["type"] = JsonValue.Create("https://tools.ietf.org/html/rfc7231#section-6.6.1"),
                            ["title"] = JsonValue.Create("Internal Server Error"),
                            ["status"] = JsonValue.Create(500),
                            ["detail"] = JsonValue.Create("An internal error occurred. Please check the logs for more details."),
                            ["traceId"] = JsonValue.Create("value")
                        }
                    }
                }
            };
        };

        return operation;
    }

    private OpenApiOperation CreateSqlDeleteOperation(
        string endpointName,
        EndpointDefinition definition,
        List<string> effectiveEnvironments,
        int operationId)
    {
        // Determine Accept content type from CustomProperties or default to application/json
        var acceptContentType = definition.CustomProperties?.GetValueOrDefault("ContentType")?.ToString() ?? "application/json";

        var operation = new OpenApiOperation
        {
            Tags = new HashSet<OpenApiTagReference> { new(definition.DocumentationTag) }, // Use DocumentationTag for proper namespace grouping (consistent with other operations)
            Summary = GetOperationSummary("DELETE", endpointName, definition),
            Description = GetOperationDescription("DELETE", endpointName, definition),
            OperationId = $"op_{operationId}",
            Parameters = new List<IOpenApiParameter>
            {
                // Environment parameter
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
                // ID parameter
                new OpenApiParameter()
                {
                    Name = "id",
                    In = ParameterLocation.Query,
                    Required = true,
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String },
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
                                Type = JsonSchemaType.Object,
                                Properties = new Dictionary<string, IOpenApiSchema>
                                {
                                    ["success"] = new OpenApiSchema { Type = JsonSchemaType.Boolean },
                                    ["message"] = new OpenApiSchema { Type = JsonSchemaType.String },
                                    ["id"] = new OpenApiSchema { Type = JsonSchemaType.String },
                                }
                            },
                            Example = new JsonObject
                            {
                                ["success"] = JsonValue.Create(true),
                                ["message"] = JsonValue.Create("Record deleted successfully"),
                                ["id"] = JsonValue.Create("744276DE-4918-4B56-AF75-16901371983B"),
                                ["result"] = new JsonObject() // leave blank
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
                                Type = JsonSchemaType.Object,
                                Properties = new Dictionary<string, IOpenApiSchema>
                                {
                                    ["error"] = new OpenApiSchema { Type = JsonSchemaType.String }
                                }
                            },
                            Example = new JsonObject
                            {
                                ["error"] = JsonValue.Create("ID parameter is required for delete operation")
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
                                Type = JsonSchemaType.Object,
                                Properties = new Dictionary<string, IOpenApiSchema>
                                {
                                    ["error"] = new OpenApiSchema { Type = JsonSchemaType.String }
                                }
                            },
                            Example = new JsonObject
                            {
                                ["error"] = JsonValue.Create("Unauthorized access. Valid Bearer token required.")
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
                                Type = JsonSchemaType.Object,
                                Properties = new Dictionary<string, IOpenApiSchema>
                                {
                                    ["error"] = new OpenApiSchema { Type = JsonSchemaType.String }
                                }
                            },
                            Example = new JsonObject
                            {
                                ["error"] = JsonValue.Create("DELETE method is not allowed for this endpoint")
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
                                ["record_not_found"] = new OpenApiExample
                                {
                                    Summary = "Record not found",
                                    Value = new JsonObject
                                    {
                                        ["error"] = JsonValue.Create("Record with specified ID not found")
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
                                Type = JsonSchemaType.Object,
                                Properties = new Dictionary<string, IOpenApiSchema>
                                {
                                    ["type"] = new OpenApiSchema { Type = JsonSchemaType.String },
                                    ["title"] = new OpenApiSchema { Type = JsonSchemaType.String },
                                    ["status"] = new OpenApiSchema { Type = JsonSchemaType.Integer },
                                    ["detail"] = new OpenApiSchema { Type = JsonSchemaType.String },
                                    ["traceId"] = new OpenApiSchema { Type = JsonSchemaType.String }
                                }
                            },
                            Example = new JsonObject
                            {
                                ["type"] = JsonValue.Create("https://tools.ietf.org/html/rfc7231#section-6.6.1"),
                                ["title"] = JsonValue.Create("Internal Server Error"),
                                ["status"] = JsonValue.Create(500),
                                ["detail"] = JsonValue.Create("An internal error occurred. Please check the logs for more details."),
                                ["traceId"] = JsonValue.Create("value")
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
        List<string> effectiveEnvironments,
        int operationId,
        EndpointDefinition? definition = null)
    {
        var operation = new OpenApiOperation
        {
            Tags = new HashSet<OpenApiTagReference> { new(definition?.DocumentationTag ?? endpointName) }, // Use DocumentationTag for consistency
            Summary = definition != null ? GetOperationSummary(method, endpointName, definition) : $"{method} {endpointName}",
            Description = definition != null ? GetOperationDescription(method, endpointName, definition) : $"Proxy {method} request to {targetUrl}",
            OperationId = $"op_{operationId}",
            Parameters = new List<IOpenApiParameter>
            {
                // Environment parameter
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
                Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                Description = "Filter expression"
            });

            // Add default $top=10 to the parameters for Proxy GET requests in documentation
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "$top",
                In = ParameterLocation.Query,
                Required = false,
                Schema = new OpenApiSchema
                {
                    Type = JsonSchemaType.Integer,
                    Default = JsonValue.Create(10)
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
                            Type = JsonSchemaType.Object
                        }
                    },
                    ["text/xml"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema { Type = JsonSchemaType.String }
                    },
                    ["application/soap+xml"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema { Type = JsonSchemaType.String }
                    },
                    ["application/xml"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema { Type = JsonSchemaType.String }
                    },
                    ["text/plain"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema { Type = JsonSchemaType.String }
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
                            Type = JsonSchemaType.Object,
                            Properties = new Dictionary<string, IOpenApiSchema>
                            {
                                ["error"] = new OpenApiSchema { Type = JsonSchemaType.String }
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
                            Type = JsonSchemaType.Object,
                            Properties = new Dictionary<string, IOpenApiSchema>
                            {
                                ["error"] = new OpenApiSchema { Type = JsonSchemaType.String },
                                ["message"] = new OpenApiSchema { Type = JsonSchemaType.String }
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

    /// <summary>
    /// Gets the effective list of allowed environments for an endpoint.
    /// Uses endpoint-specific AllowedEnvironments if defined, otherwise falls back to global settings.
    /// </summary>
    private List<string> GetEffectiveEnvironments(EndpointDefinition? definition)
    {
        if (definition?.AllowedEnvironments != null && definition.AllowedEnvironments.Any())
        {
            return definition.AllowedEnvironments;
        }
        return GetAllowedEnvironments();
    }

    private HttpMethod? GetOperationType(string method)
    {
        return method.ToUpperInvariant() switch
        {
            "GET" => HttpMethod.Get,
            "POST" => HttpMethod.Post,
            "PUT" => HttpMethod.Put,
            "DELETE" => HttpMethod.Delete,
            "PATCH" => HttpMethod.Patch,
            "OPTIONS" => HttpMethod.Options,
            "HEAD" => HttpMethod.Head,
            "MERGE" => HttpMethod.Patch, // Map MERGE to PATCH as they're semantically similar
            _ => null
        };
    }

    private void AddOperationToPath(IOpenApiPathItem pathItem, string method, OpenApiOperation operation)
    {
        var operationType = GetOperationType(method);
        if (operationType != null && pathItem.Operations != null)
        {
            pathItem.Operations[operationType] = operation;
        }
    }

    /// <summary>
    /// Gets the appropriate OpenAPI schema type for a given content type
    /// </summary>
    private static OpenApiSchema GetSchemaForContentType(string contentType)
    {
        if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            return new OpenApiSchema { Type = JsonSchemaType.Object };

        return new OpenApiSchema { Type = JsonSchemaType.String };
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

        // Use Documentation.Summary if available for better descriptions
        if (!string.IsNullOrWhiteSpace(definition.Documentation?.Summary))
        {
            var summary = definition.Documentation.Summary.Trim();

            // Check if summary already starts with an action verb - if so, use it as-is for GET
            var startsWithActionVerb = summary.StartsWith("Retrieve", StringComparison.OrdinalIgnoreCase) ||
                                     summary.StartsWith("Get", StringComparison.OrdinalIgnoreCase) ||
                                     summary.StartsWith("Fetch", StringComparison.OrdinalIgnoreCase) ||
                                     summary.StartsWith("Load", StringComparison.OrdinalIgnoreCase);

            return method.ToUpper() switch
            {
                "GET" => startsWithActionVerb ? summary : $"Retrieve {summary.ToLower()}",
                "POST" => $"Create new {summary.ToLower()}",
                "PUT" => $"Update {summary.ToLower()}",
                "PATCH" => $"Partially update {summary.ToLower()}",
                "DELETE" => $"Delete {summary.ToLower()}",
                _ => $"{method} {summary}"
            };
        }

        // Return default format using DisplayName if available
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

        // Use Documentation.Description if available for better descriptions
        if (!string.IsNullOrWhiteSpace(definition.Documentation?.Description))
        {
            return definition.Documentation.Description;
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
    private void AddTagsToDocument(OpenApiDocument document, Dictionary<string, string> documentTags)
    {
        // Initialize tags collection if it doesn't exist
        document.Tags ??= new HashSet<OpenApiTag>();

        // Add each tag with its description (sorting will be handled by AlphabeticalEndpointSorter)
        foreach (var tagEntry in documentTags)
        {
            var existingTag = document.Tags.FirstOrDefault(t => string.Equals(t.Name, tagEntry.Key, StringComparison.OrdinalIgnoreCase));
            if (existingTag == null)
            {
                document.Tags.Add(new OpenApiTag
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
        if (document.Paths.Any(p => p.Key.Contains("/webhook/")))
        {
            var webhookTag = document.Tags.FirstOrDefault(t => string.Equals(t.Name, "Webhook", StringComparison.OrdinalIgnoreCase));
            if (webhookTag == null)
            {
                var webhookDocumentation = LoadWebhookDocumentation();
                document.Tags.Add(new OpenApiTag
                {
                    Name = "Webhook",
                    Description = webhookDocumentation?.TagDescription ?? "Endpoints for receiving and processing external webhook events"
                });
            }
        }

        // Sort all tags alphabetically
        document.Tags = new HashSet<OpenApiTag>(document.Tags.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase));
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
                _logger.LogWarning("Webhook entity.json not found at: {Path}", webhookEntityPath);
                return null;
            }

            string json = File.ReadAllText(webhookEntityPath);
            var webhookEntity = JsonSerializer.Deserialize<WebhookEntity>(json);
            return webhookEntity?.Documentation;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading webhook documentation");
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
