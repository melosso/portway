using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace PortwayApi.Helpers;

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

    // Controller-free overloads for handlers that run outside ControllerBase
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

    public static IActionResult ServerError(string detail)
        => new ObjectResult(new ProblemDetails
        {
            Detail = detail,
            Status = StatusCodes.Status500InternalServerError,
            Title = "Error"
        })
        { StatusCode = StatusCodes.Status500InternalServerError };

    public static IActionResult ServerError(HttpContext context, string detail, string title = "Error")
        => ProblemWithTrace(context, detail, title);

    /// <summary>Replicates ControllerBase.Problem's 500 shape (type link and traceId) outside a controller</summary>
    public static IActionResult ProblemWithTrace(HttpContext context, string detail, string title)
    {
        var problem = new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
            Title = title,
            Status = StatusCodes.Status500InternalServerError,
            Detail = detail
        };
        problem.Extensions["traceId"] = System.Diagnostics.Activity.Current?.Id ?? context.TraceIdentifier;
        return new ObjectResult(problem) { StatusCode = StatusCodes.Status500InternalServerError };
    }

    public static IActionResult Create(string location, string message, object? result = null, object? id = null)
        => new CreatedResult(location, CreatedResponse.Of(message, result, id));

    public static IActionResult ValidationFailed(IEnumerable<ValidationDetail> details, string error = "Validation failed")
        => new UnprocessableEntityObjectResult(ValidationErrorResponse.Of(details, error));
}
