namespace PortwayApi.Classes.OpenApi;

using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using System.Linq;

/// <summary>Shared error-response schemas and a helper to attach a standard set of error responses to an operation</summary>
public static class StandardResponses
{
    public const string ErrorSchemaId = "ErrorResponse";
    public const string ValidationSchemaId = "ValidationErrorResponse";
    public const string ErrorMediaTypeId = "ErrorJson";
    public const string ValidationMediaTypeId = "ValidationErrorJson";

    private static readonly FrozenDictionary<int, string> Summaries = new Dictionary<int, string>
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
        [500] = "Internal Server Error",
        [503] = "Service Unavailable"
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<int, string> Descriptions = new Dictionary<int, string>
    {
        [200] = "The request succeeded and the payload is in the response body.",
        [201] = "The record was created.",
        [202] = "The request was accepted for processing and has not finished yet.",
        [204] = "The request succeeded and returns no body.",
        [206] = "The requested byte range of the file was returned.",
        [304] = "The cached copy is still current, so no body is returned.",
        [400] = "The request was malformed or failed the validation rules configured for this endpoint.",
        [401] = "The bearer token is missing, expired or invalid.",
        [403] = "The token is valid but is not authorized for this endpoint or environment.",
        [404] = "No endpoint or record matches the request.",
        [405] = "This endpoint does not allow the HTTP method that was used.",
        [406] = "No representation matching the Accept header is available.",
        [409] = "The request conflicts with the current state of the record.",
        [413] = "The payload is larger than the limit configured for this endpoint.",
        [415] = "The Content-Type of the request is not supported by this endpoint.",
        [416] = "The requested byte range falls outside the file.",
        [422] = "The payload is well formed but one or more fields failed validation.",
        [500] = "The gateway or an upstream failed; traceId correlates the failure with the server log.",
        [503] = "The endpoint is disabled or its upstream is unreachable."
    }.ToFrozenDictionary();

    /// <summary>The HTTP reason phrase for a status code, or null when none is defined</summary>
    public static string? SummaryFor(int code) => Summaries.TryGetValue(code, out var s) ? s : null;

    /// <summary>What the status code means for a Portway endpoint, or null when none is defined</summary>
    public static string? DescriptionFor(int code) => Descriptions.TryGetValue(code, out var d) ? d : null;

    /// <summary>Registers the shared { success, error } and validation schemas, plus the media types wrapping them, as reusable components (once)</summary>
    public static void EnsureSchemas(OpenApiDocument document)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
        document.Components.MediaTypes ??= new Dictionary<string, IOpenApiMediaType>();

        document.Components.MediaTypes.TryAdd(ErrorMediaTypeId, new OpenApiMediaType
        {
            Schema = new OpenApiSchemaReference(ErrorSchemaId, document)
        });

        document.Components.MediaTypes.TryAdd(ValidationMediaTypeId, new OpenApiMediaType
        {
            Schema = new OpenApiSchemaReference(ValidationSchemaId, document)
        });

        if (!document.Components.Schemas.ContainsKey(ErrorSchemaId))
        {
            document.Components.Schemas[ErrorSchemaId] = new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                Description = "Standard error envelope returned by all endpoint types",
                Properties = new Dictionary<string, IOpenApiSchema>
                {
                    ["success"] = new OpenApiSchema { Type = JsonSchemaType.Boolean },
                    ["error"] = new OpenApiSchema { Type = JsonSchemaType.String },
                    ["traceId"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String,
                        Description = "Unique ID correlating internal server errors with logs. Included only on 500 status code."
                    }
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

    /// <summary>Adds the error responses this operation kind documents, per the shared error-code matrix</summary>
    public static void AddErrors(OpenApiOperation operation, ApiOperationKind kind)
        => AddErrors(operation, StandardErrorCodes.For(kind));

    /// <summary>Replaces every error response on an operation with the given codes, each referencing the shared schema (422 uses the validation schema)</summary>
    public static void AddErrors(OpenApiOperation operation, params int[] codes)
    {
        operation.Responses ??= new OpenApiResponses();

        // Drop anything a builder declared inline so the shared envelope is the only error shape
        var declaredErrors = operation.Responses.Keys
            .Where(k => int.TryParse(k, out var status) && status >= 400)
            .ToList();

        foreach (var key in declaredErrors)
        {
            operation.Responses.Remove(key);
        }

        foreach (var code in codes)
        {
            var mediaTypeId = code == 422 ? ValidationMediaTypeId : ErrorMediaTypeId;
            operation.Responses[code.ToString()] = new OpenApiResponse
            {
                Summary = SummaryFor(code),
                Description = DescriptionFor(code) ?? "Error",
                Content = new Dictionary<string, IOpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaTypeReference(mediaTypeId)
                }
            };
        }
    }
}
