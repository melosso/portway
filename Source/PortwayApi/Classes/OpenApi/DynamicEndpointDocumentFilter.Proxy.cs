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
                    var requestContent = new Dictionary<string, IOpenApiMediaType>();

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
                        Content = new Dictionary<string, IOpenApiMediaType>
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
                Content = new Dictionary<string, IOpenApiMediaType>
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
                        }
                    }
                }
            },
            ["403"] = new OpenApiResponse { Description = "Forbidden - Insufficient permissions" },
            ["404"] = new OpenApiResponse { Description = "Not Found - Resource not found" },
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
                                ["error"] = new OpenApiSchema { Type = JsonSchemaType.String },
                                ["message"] = new OpenApiSchema { Type = JsonSchemaType.String }
                            }
                        }
                    }
                }
            }
        };

        // Standardize error responses onto the shared schema (validated matrix)
        foreach (var c in new[] { "400", "401", "403", "404", "405", "406", "409", "413", "415", "416", "422", "500" })
            operation.Responses.Remove(c);
        StandardResponses.AddErrors(operation, 400, 401, 403, 404, 500);

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

    /// <summary>Gets the effective list of allowed environments for an endpoint. Uses endpoint-specific AllowedEnvironments if defined, otherwise falls back to global settings</summary>
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
            "QUERY" => HttpMethod.Parse("QUERY"), // OpenAPI 3.2 renders this as a native query operation
            _ => null
        };
    }
}
