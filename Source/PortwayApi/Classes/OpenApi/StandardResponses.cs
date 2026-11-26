namespace PortwayApi.Classes.OpenApi;

using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using System.Linq;

/// <summary>
/// Shared error-response schemas and a helper to attach a standard set of error responses to an operation
/// </summary>
public static class StandardResponses
{
    public const string ErrorSchemaId = "ErrorResponse";
    public const string ValidationSchemaId = "ValidationErrorResponse";
    public const string ErrorMediaTypeId = "ErrorJson";
    public const string ValidationMediaTypeId = "ValidationErrorJson";
    public const string RateLimitSchemaId = "RateLimitErrorResponse";
    public const string RateLimitMediaTypeId = "RateLimitErrorJson";
    public const string CompositeSchemaId = "CompositeErrorResponse";
    public const string CompositeMediaTypeId = "CompositeErrorJson";
    private const string CompositePrefix = "Composite";

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
        [429] = "Too Many Requests",
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
        [429] = "The rate limit for this token or client address is exhausted; retry after the Retry-After interval.",
        [500] = "The request failed; traceId correlates the failure with the server log.",
        [503] = "The endpoint is unavailable."
    }.ToFrozenDictionary();

    /// <summary>
    /// The HTTP reason phrase for a status code, or null when none is defined
    /// </summary>
    public static string? SummaryFor(int code) => Summaries.TryGetValue(code, out var s) ? s : null;

    /// <summary>
    /// What the status code means for a Portway endpoint, or null when none is defined
    /// </summary>
    public static string? DescriptionFor(int code) => Descriptions.TryGetValue(code, out var d) ? d : null;

    /// <summary>
    /// Component name of the shared error response for a status code, the reason phrase without spaces
    /// </summary>
    public static string ResponseIdFor(int code) => (SummaryFor(code) ?? $"Error{code}").Replace(" ", "");

    /// <summary>
    /// Registers the shared { success, error } and validation schemas, the media types wrapping them and one error response per status code, as reusable components (once)
    /// </summary>
    public static void EnsureSchemas(OpenApiDocument document)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
        document.Components.MediaTypes ??= new Dictionary<string, IOpenApiMediaType>();
        document.Components.Responses ??= new Dictionary<string, IOpenApiResponse>();

        foreach (var code in StandardErrorCodes.All)
        {
            document.Components.Responses.TryAdd(ResponseIdFor(code), ErrorResponseFor(code, code switch
            {
                422 => ValidationMediaTypeId,
                429 => RateLimitMediaTypeId,
                _ => ErrorMediaTypeId
            }, document));
        }

        foreach (var code in StandardErrorCodes.For(ApiOperationKind.Composite).Where(UsesCompositeEnvelope))
        {
            document.Components.Responses.TryAdd(CompositePrefix + ResponseIdFor(code), ErrorResponseFor(code, CompositeMediaTypeId, document));
        }

        document.Components.MediaTypes.TryAdd(RateLimitMediaTypeId, new OpenApiMediaType
        {
            Schema = new OpenApiSchemaReference(RateLimitSchemaId, document)
        });

        document.Components.MediaTypes.TryAdd(CompositeMediaTypeId, new OpenApiMediaType
        {
            Schema = new OpenApiSchemaReference(CompositeSchemaId, document)
        });

        document.Components.Schemas.TryAdd(RateLimitSchemaId, new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Description = "Rate limit error envelope (429)",
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["success"] = new OpenApiSchema { Type = JsonSchemaType.Boolean },
                ["error"] = new OpenApiSchema { Type = JsonSchemaType.String },
                ["retrytime"] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "date-time", Description = "The moment the limit resets, the same instant as Retry-After" }
            },
            Required = new HashSet<string> { "success", "error", "retrytime" },
            Examples = [new JsonObject { ["success"] = false, ["error"] = "Too many requests", ["retrytime"] = "2026-01-01T12:00:30.0000000+00:00" }]
        });

        document.Components.Schemas.TryAdd(CompositeSchemaId, new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Description = "Composite error envelope; step failures add the failing step and the steps completed before it",
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["success"] = new OpenApiSchema { Type = JsonSchemaType.Boolean },
                ["error"] = new OpenApiSchema { Type = JsonSchemaType.String },
                ["details"] = new OpenApiSchema { Type = JsonSchemaType.String, Description = "Gateway description of the step failure" },
                ["traceId"] = new OpenApiSchema { Type = JsonSchemaType.String },
                ["step"] = new OpenApiSchema { Type = JsonSchemaType.String },
                ["statusCode"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Description = "Status returned by the failing step" },
                ["completedSteps"] = new OpenApiSchema { Type = JsonSchemaType.Array, Items = new OpenApiSchema { Type = JsonSchemaType.String } }
            },
            Required = new HashSet<string> { "success", "error" },
            Examples =
            [
                new JsonObject
                {
                    ["success"] = false,
                    ["error"] = "Error executing step 'CreateOrderHeader'",
                    ["details"] = "Endpoint 'Sales/OrderHeader' not found",
                    ["traceId"] = "00-530e2d01e7e446f7c1a9936cd2858df4-77e01a3151145562-02",
                    ["step"] = "CreateOrderHeader",
                    ["statusCode"] = 404,
                    ["completedSteps"] = new JsonArray { "CreateOrderLines" }
                }
            ]
        });

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
                    },
                    ["field"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String,
                        Description = "Name of the input that caused the error, if one input caused it."
                    }
                },
                Required = new HashSet<string> { "success", "error" },
                Examples = [new JsonObject { ["success"] = false, ["error"] = "A human-readable message" }]
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
                Examples =
                [
                    new JsonObject
                    {
                        ["success"] = false,
                        ["error"] = "Validation failed",
                        ["details"] = new JsonArray { new JsonObject { ["field"] = "Price", ["message"] = "is required" } }
                    }
                ]
            };
        }
    }

    /// <summary>
    /// Adds the error responses this operation kind documents, per the shared error-code matrix
    /// </summary>
    public static void AddErrors(OpenApiOperation operation, ApiOperationKind kind)
    {
        AddErrors(operation, StandardErrorCodes.For(kind));

        if (kind != ApiOperationKind.Composite)
        {
            return;
        }

        foreach (var code in StandardErrorCodes.For(kind).Where(UsesCompositeEnvelope))
        {
            operation.Responses![code.ToString()] = new OpenApiResponseReference(CompositePrefix + ResponseIdFor(code));
        }
    }

    private static bool UsesCompositeEnvelope(int code) => code is not (401 or 429);

    private static OpenApiResponse ErrorResponseFor(int code, string mediaTypeId, OpenApiDocument document) => new()
    {
        Summary = SummaryFor(code),
        Description = DescriptionFor(code),
        Headers = code switch
        {
            401 => new Dictionary<string, IOpenApiHeader>
            {
                ["WWW-Authenticate"] = new OpenApiHeader
                {
                    Description = "Bearer challenge (RFC 6750); error=\"invalid_token\" when a token was sent and rejected",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String }
                }
            },
            429 => new Dictionary<string, IOpenApiHeader>
            {
                ["Retry-After"] = new OpenApiHeader
                {
                    Description = "Seconds to wait before retrying",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.Integer }
                }
            },
            _ => null
        },
        Content = new Dictionary<string, IOpenApiMediaType>
        {
            ["application/json"] = new OpenApiMediaTypeReference(mediaTypeId, document)
        }
    };

    /// <summary>
    /// Replaces every error response on an operation with references to the shared error responses for the given codes
    /// </summary>
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
            operation.Responses[code.ToString()] = new OpenApiResponseReference(ResponseIdFor(code));
        }
    }
}
