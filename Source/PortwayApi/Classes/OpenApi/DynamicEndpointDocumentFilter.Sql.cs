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

            // Document exactly the endpoint's declared methods. Never mutate the shared definition, and
            // let GetOperationType decide what is renderable, so unknown verbs (like QUERY, until OpenAPI 3.2)
            // are skipped instead of being misrendered as GET.
            foreach (var method in definition.Methods)
            {
                if (method.Equals("DELETE", StringComparison.OrdinalIgnoreCase))
                    continue; // DELETE gets its own OData-keyed path below

                if (GetOperationType(method) == null)
                    continue; // unrenderable verb; skip without inventing a GET

                var operation = CreateSqlOperation(
                    endpointName,
                    method,
                    definition,
                    effectiveEnvironments,
                    operationIdCounter++);

                AddOperationToPath(document.Paths[path], method, operation);
            }

            // Drop the base path if none of the declared methods are renderable (e.g. a QUERY-only endpoint)
            if (document.Paths[path].Operations!.Count == 0)
            {
                document.Paths.Remove(path);
            }

            // Add specific delete endpoint with OData-style ID in path
            if (definition.Methods.Contains("DELETE", StringComparer.OrdinalIgnoreCase))
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
            var requestContent = new Dictionary<string, IOpenApiMediaType>
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

        else if (method.Equals("QUERY", StringComparison.OrdinalIgnoreCase))
        {
            // QUERY (RFC 10008): the OData query travels in the JSON request body instead of the URL
            operation.RequestBody = new OpenApiRequestBody
            {
                Description = "Query criteria (RFC 10008). Send OData-style fields in the body instead of the URL",
                Required = false,
                Content = new Dictionary<string, IOpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = JsonSchemaType.Object,
                            Properties = new Dictionary<string, IOpenApiSchema>
                            {
                                ["select"]  = new OpenApiSchema { Type = JsonSchemaType.String, Description = "Comma-separated fields to return" },
                                ["filter"]  = new OpenApiSchema { Type = JsonSchemaType.String, Description = "OData $filter expression" },
                                ["orderby"] = new OpenApiSchema { Type = JsonSchemaType.String, Description = "OData $orderby expression" },
                                ["top"]     = new OpenApiSchema { Type = JsonSchemaType.Integer, Default = JsonValue.Create(10) },
                                ["skip"]    = new OpenApiSchema { Type = JsonSchemaType.Integer, Default = JsonValue.Create(0) }
                            }
                        }
                    }
                }
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
                Content = new Dictionary<string, IOpenApiMediaType>
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
                Content = new Dictionary<string, IOpenApiMediaType>
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
                                    ["success"] = new OpenApiSchema { Type = JsonSchemaType.Boolean },
                                    ["count"] = new OpenApiSchema { Type = JsonSchemaType.Integer },
                                    ["value"] = new OpenApiSchema
                                    {
                                        Type = JsonSchemaType.Array,
                                        Items = new OpenApiSchema { Type = JsonSchemaType.Object }
                                    },
                                    ["nextLink"] = new OpenApiSchema { Type = JsonSchemaType.String }
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
                                        ["success"] = JsonValue.Create(true),
                                        ["count"] = JsonValue.Create(0),
                                        ["value"] = new JsonArray(),
                                        ["nextLink"] = JsonValue.Create($"/api/{endpointName}?$top=10&$skip=0")
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
        };

        // Standardize error responses onto the shared schema (validated matrix; removes over-promised codes)
        foreach (var c in new[] { "400", "401", "403", "404", "405", "406", "409", "413", "415", "416", "422", "500" })
            operation.Responses.Remove(c);
        var isWrite = method.Equals("POST", StringComparison.OrdinalIgnoreCase)
            || method.Equals("PUT", StringComparison.OrdinalIgnoreCase)
            || method.Equals("PATCH", StringComparison.OrdinalIgnoreCase)
            || method.Equals("MERGE", StringComparison.OrdinalIgnoreCase);
        var codes = new List<int> { 400, 401, 403, 404, 500 };
        if (isWrite) codes.Add(422);
        if (method.Equals("QUERY", StringComparison.OrdinalIgnoreCase)) codes.Add(415);
        StandardResponses.AddErrors(operation, codes.ToArray());

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
                                ["error"] = JsonValue.Create("ID parameter is required for delete operation")
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
                                ["error"] = JsonValue.Create("Unauthorized access. Valid Bearer token required.")
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
                                ["error"] = JsonValue.Create("DELETE method is not allowed for this endpoint")
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

        // Standardize error responses onto the shared schema (validated matrix)
        foreach (var c in new[] { "400", "401", "403", "404", "405", "406", "409", "413", "415", "416", "422", "500" })
            operation.Responses.Remove(c);
        StandardResponses.AddErrors(operation, 400, 401, 403, 404, 500);

        return operation;
    }
}
