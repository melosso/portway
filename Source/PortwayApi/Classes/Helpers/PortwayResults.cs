using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace PortwayApi.Classes.Helpers;

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
