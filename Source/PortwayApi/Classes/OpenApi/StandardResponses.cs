namespace PortwayApi.Classes;

using System.Collections.Generic;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;

/// <summary>Shared error-response schemas and a helper to attach a standard set of error responses to an operation</summary>
public static class StandardResponses
{
    public const string ErrorSchemaId = "ErrorResponse";
    public const string ValidationSchemaId = "ValidationErrorResponse";

    private static readonly Dictionary<int, string> Descriptions = new()
    {
        [200] = "OK",
        [201] = "Created",
        [202] = "Accepted",
        [204] = "No Content",
        [206] = "Partial Content",
        [304] = "Not Modified",
        [400] = "Bad Request",
        [401] = "Unauthorized",
        [403] = "Forbidden",
        [404] = "Not Found",
        [405] = "Method Not Allowed",
        [406] = "Not Acceptable",
        [409] = "Conflict",
        [413] = "Content Too Large",
        [415] = "Unsupported Media Type",
        [416] = "Range Not Satisfiable",
        [422] = "Unprocessable Content",
        [500] = "Internal Server Error"
    };

    /// <summary>The standard description for a status code, or null when none is defined</summary>
    public static string? DescriptionFor(int code) => Descriptions.TryGetValue(code, out var d) ? d : null;

    /// <summary>Registers the shared { success, error } and validation schemas as reusable components (once)</summary>
    public static void EnsureSchemas(OpenApiDocument document)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();

        if (!document.Components.Schemas.ContainsKey(ErrorSchemaId))
        {
            document.Components.Schemas[ErrorSchemaId] = new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                Description = "Standard error envelope returned by all endpoint types",
                Properties = new Dictionary<string, IOpenApiSchema>
                {
                    ["success"] = new OpenApiSchema { Type = JsonSchemaType.Boolean },
                    ["error"] = new OpenApiSchema { Type = JsonSchemaType.String }
                },
                Required = new HashSet<string> { "success", "error" },
                Example = new JsonObject { ["success"] = false, ["error"] = "A human-readable message" }
            };
        }

        if (!document.Components.Schemas.ContainsKey(ValidationSchemaId))
        {
            document.Components.Schemas[ValidationSchemaId] = new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                Description = "Validation error envelope (422) with per-field details",
                Properties = new Dictionary<string, IOpenApiSchema>
                {
                    ["success"] = new OpenApiSchema { Type = JsonSchemaType.Boolean },
                    ["error"] = new OpenApiSchema { Type = JsonSchemaType.String },
                    ["details"] = new OpenApiSchema
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
                },
                Required = new HashSet<string> { "success", "error" },
                Example = new JsonObject
                {
                    ["success"] = false,
                    ["error"] = "Validation failed",
                    ["details"] = new JsonArray { new JsonObject { ["field"] = "Price", ["message"] = "is required" } }
                }
            };
        }
    }

    /// <summary>Adds the given error responses to an operation, each referencing the shared schema (422 uses the validation schema)</summary>
    public static void AddErrors(OpenApiOperation operation, params int[] codes)
    {
        operation.Responses ??= new OpenApiResponses();

        foreach (var code in codes)
        {
            var schemaId = code == 422 ? ValidationSchemaId : ErrorSchemaId;
            operation.Responses[code.ToString()] = new OpenApiResponse
            {
                Description = DescriptionFor(code) ?? "Error",
                Content = new Dictionary<string, IOpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchemaReference(schemaId)
                    }
                }
            };
        }
    }
}
