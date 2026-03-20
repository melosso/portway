using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace PortwayApi.Classes.Helpers;

// Collection responses (SQL GET, Static filtered, File list)
public sealed record CollectionResponse<T>(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("count")]   int Count,
    [property: JsonPropertyName("value")]   IReadOnlyList<T> Value,
    [property: JsonPropertyName("nextLink")] string? NextLink = null)
{
    public static CollectionResponse<T> Of(IReadOnlyList<T> items, string? nextLink = null)
        => new(true, items.Count, items, nextLink);
}

// Mutation success (PUT, PATCH, DELETE, Composite fallback)
public sealed record MutationResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("result")]  object? Result = null)
{
    public static MutationResponse Of(string message, object? result = null)
        => new(true, message, result);
}

// 201 Created (SQL POST, Webhook POST)
public sealed record CreatedResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("result")]  object? Result = null,
    [property: JsonPropertyName("id")]      object? Id = null)
{
    public static CreatedResponse Of(string message, object? result = null, object? id = null)
        => new(true, message, result, id);
}

// 201 Created — File upload (distinct shape per HTTP semantics)
public sealed record FileCreatedResponse(
    [property: JsonPropertyName("success")]     bool   Success,
    [property: JsonPropertyName("fileId")]      string FileId,
    [property: JsonPropertyName("filename")]    string Filename,
    [property: JsonPropertyName("contentType")] string ContentType,
    [property: JsonPropertyName("size")]        long   Size,
    [property: JsonPropertyName("url")]         string Url)
{
    public static FileCreatedResponse Of(
        string fileId, string filename, string contentType, long size, string url)
        => new(true, fileId, filename, contentType, size, url);
}

// All non-validation errors
public sealed record ErrorResponse(
    [property: JsonPropertyName("success")] bool   Success,
    [property: JsonPropertyName("error")]   string Error)
{
    public static ErrorResponse Of(string error) => new(false, error);
}

// 422 Validation detail item
public sealed record ValidationDetail(
    [property: JsonPropertyName("field")]   string Field,
    [property: JsonPropertyName("message")] string Message);

// 422 Validation error
public sealed record ValidationErrorResponse(
    [property: JsonPropertyName("success")] bool   Success,
    [property: JsonPropertyName("error")]   string Error,
    [property: JsonPropertyName("details")] IReadOnlyList<ValidationDetail> Details)
{
    public static ValidationErrorResponse Of(
        IEnumerable<ValidationDetail> details, string error = "Validation failed")
        => new(false, error, [.. details]);
}

public static class PortwayResults
{
    public static IActionResult Collection<T>(ControllerBase ctrl,
        IReadOnlyList<T> items, string? nextLink = null)
        => ctrl.Ok(CollectionResponse<T>.Of(items, nextLink));

    public static IActionResult Mutation(ControllerBase ctrl,
        string message, object? result = null)
        => ctrl.Ok(MutationResponse.Of(message, result));

    public static IActionResult Create(ControllerBase ctrl,
        string location, string message, object? result = null, object? id = null)
        => ctrl.Created(location, CreatedResponse.Of(message, result, id));

    public static IActionResult FileCreate(ControllerBase ctrl,
        string location, string fileId, string filename,
        string contentType, long size, string url)
        => ctrl.Created(location, FileCreatedResponse.Of(fileId, filename, contentType, size, url));

    public static IActionResult BadRequest(ControllerBase ctrl, string error)
        => ctrl.BadRequest(ErrorResponse.Of(error));

    public static IActionResult NotFound(ControllerBase ctrl, string error)
        => ctrl.NotFound(ErrorResponse.Of(error));

    public static IActionResult MethodNotAllowed(ControllerBase ctrl,
        string error = "Method not allowed")
        => ctrl.StatusCode(405, ErrorResponse.Of(error));

    public static IActionResult NotAcceptable(ControllerBase ctrl, string error)
        => ctrl.StatusCode(406, ErrorResponse.Of(error));

    public static IActionResult Conflict(ControllerBase ctrl, string error)
        => ctrl.Conflict(ErrorResponse.Of(error));

    public static IActionResult UnsupportedMediaType(ControllerBase ctrl, string error)
        => ctrl.StatusCode(415, ErrorResponse.Of(error));

    public static IActionResult ValidationFailed(ControllerBase ctrl,
        IEnumerable<ValidationDetail> details, string error = "Validation failed")
        => ctrl.UnprocessableEntity(ValidationErrorResponse.Of(details, error));

    public static IActionResult ServerError(ControllerBase ctrl, string detail)
        => ctrl.Problem(detail: detail,
                        statusCode: StatusCodes.Status500InternalServerError,
                        title: "Error");
}
