using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace PortwayApi.Helpers;

public static class PortwayResults
{
    /// <summary>503 with a Retry-After hint; needs the controller to reach the response headers</summary>
    public static IActionResult ServiceUnavailable(ControllerBase ctrl, string error, int? retryAfterSeconds = null)
    {
        SetRetryAfter(ctrl.Response, retryAfterSeconds);
        return ctrl.StatusCode(StatusCodes.Status503ServiceUnavailable, ErrorResponse.Of(error));
    }

    public static IActionResult Collection<T>(IReadOnlyList<T> items, string? nextLink = null)
        => new OkObjectResult(CollectionResponse<T>.Of(items, nextLink));

    public static IActionResult Mutation(string message, object? result = null)
        => new OkObjectResult(MutationResponse.Of(message, result));

    public static IActionResult BadRequest(string error)
        => new BadRequestObjectResult(ErrorResponse.Of(error));

    public static IActionResult NotFound(string error)
        => new NotFoundObjectResult(ErrorResponse.Of(error));

    public static IActionResult MethodNotAllowed(string error = "Method not allowed")
        => new ObjectResult(ErrorResponse.Of(error)) { StatusCode = 405 };

    public static IActionResult NotAcceptable(string error)
        => new ObjectResult(ErrorResponse.Of(error)) { StatusCode = 406 };

    public static IActionResult Conflict(string error)
        => new ConflictObjectResult(ErrorResponse.Of(error));

    public static IActionResult UnsupportedMediaType(string error)
        => new ObjectResult(ErrorResponse.Of(error)) { StatusCode = 415 };

    public static IActionResult ServerError(string detail)
        => new ObjectResult(ErrorResponse.Of(detail)) { StatusCode = StatusCodes.Status500InternalServerError };

    /// <summary>500 in the shared envelope, carrying the trace id that correlates it with the server log</summary>
    public static IActionResult ServerError(HttpContext context, string detail)
        => new ObjectResult(ErrorResponse.Traced(detail, TraceIdOf(context)))
        { StatusCode = StatusCodes.Status500InternalServerError };

    /// <summary>Masked 500 in the shared envelope for handlers that return minimal-API results</summary>
    public static IResult MinimalServerError(HttpContext context, string detail = "An unexpected error occurred.")
        => Results.Json(ErrorResponse.Traced(detail, TraceIdOf(context)),
                        statusCode: StatusCodes.Status500InternalServerError);

    /// <summary>Trace id a caller can quote when reporting a masked error</summary>
    public static string TraceIdOf(HttpContext? context)
        => System.Diagnostics.Activity.Current?.Id ?? context?.TraceIdentifier ?? string.Empty;

    public static IActionResult Create(string location, string message, object? result = null, object? id = null)
        => new CreatedResult(location, CreatedResponse.Of(message, result, id));

    public static IActionResult FileCreate(string location, string fileId, string filename,
        string contentType, long size, string url)
        => new CreatedResult(location, FileCreatedResponse.Of(fileId, filename, contentType, size, url));

    public static IActionResult ValidationFailed(IEnumerable<ValidationDetail> details, string error = "Validation failed")
        => new UnprocessableEntityObjectResult(ValidationErrorResponse.Of(details, error));

    /// <summary>503 without a Retry-After; use the ControllerBase overload or set the header yourself when a hint is needed</summary>
    public static IActionResult ServiceUnavailable(string error)
        => new ObjectResult(ErrorResponse.Of(error)) { StatusCode = StatusCodes.Status503ServiceUnavailable };

    /// <summary>Tells clients and caches how long to back off before retrying a deliberate outage</summary>
    private static void SetRetryAfter(HttpResponse response, int? retryAfterSeconds)
    {
        if (retryAfterSeconds is > 0)
        {
            response.Headers.RetryAfter = retryAfterSeconds.Value.ToString();
        }
    }
}
