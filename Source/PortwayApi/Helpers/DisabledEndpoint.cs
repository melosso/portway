namespace PortwayApi.Helpers;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

/// <summary>The single response for an endpoint switched off through its Enabled flag</summary>
public static class DisabledEndpoint
{
    public const string Message = "This endpoint is temporarily disabled for scheduled maintenance.";

    public const int RetryAfterSeconds = 3600;

    /// <summary>503 for controller-served endpoints</summary>
    public static IActionResult Result(ControllerBase controller)
        => PortwayResults.ServiceUnavailable(controller, Message, RetryAfterSeconds);

    /// <summary>503 for handlers that return minimal-API results</summary>
    public static IResult MinimalResult(HttpContext context)
    {
        context.Response.Headers.RetryAfter = RetryAfterSeconds.ToString();
        return Results.Json(ErrorResponse.Of(Message), statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
