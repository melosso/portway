namespace PortwayApi.Classes.OpenApi;

using System.Collections.Generic;
using Microsoft.OpenApi;

/// <summary>Shared error-response schemas and a helper to attach a standard set of error responses to an operation</summary>
public static class StandardResponses
{
    public const string ErrorSchemaId = "ErrorResponse";
    public const string ValidationSchemaId = "ValidationErrorResponse";

    private static readonly Dictionary<int, string> Descriptions = new()
    {
        [400] = "Bad Request - the request was malformed or the environment is not allowed",
        [401] = "Unauthorized - a valid authentication token is required",
        [403] = "Forbidden - the token is valid but lacks permission, or the target was blocked",
        [404] = "Not Found - the endpoint or resource does not exist",
        [405] = "Method Not Allowed - this method is not enabled for the endpoint",
        [406] = "Not Acceptable - the requested content type cannot be served",
        [409] = "Conflict - the resource already exists",
        [413] = "Payload Too Large - the uploaded content exceeds the allowed size",
        [415] = "Unsupported Media Type - the content type or file extension is not accepted",
        [416] = "Range Not Satisfiable - the requested byte range is invalid",
        [422] = "Unprocessable Content - the payload failed validation",
        [500] = "Internal Server Error - an unexpected error occurred"
    };

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
                Required = new HashSet<string> { "success", "error" }
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
                Required = new HashSet<string> { "success", "error" }
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
                Description = Descriptions.TryGetValue(code, out var d) ? d : "Error",
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
