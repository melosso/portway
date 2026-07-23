using Microsoft.AspNetCore.Mvc;
using PortwayApi.Classes;

namespace PortwayApi.Api;

public partial class EndpointController
{
    /// <summary>Handles Static GET requests</summary>
    private async Task<IActionResult> HandleStaticGetRequest(
        string env,
        string endpointName,
        string? select,
        string? filter,
        string? orderby,
        int top,
        int skip)
    {
        try
        {
            if (TryResolveEndpoint(EndpointType.Static, endpointName, null, out var endpoint) is { } resolveError)
            {
                return resolveError;
            }

            return await _staticRequestHandler.HandleAsync(HttpContext, endpoint, env, endpointName, select, filter, orderby, top, skip);
        }
        catch (Exception ex)
        {
            return HandleUnexpectedError(ex, "static content", endpointName, "Error serving static content");
        }
    }
}
