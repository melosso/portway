using Microsoft.AspNetCore.Mvc;

namespace PortwayApi.Api;

public partial class EndpointController
{
    /// <summary>
    /// Handles composite endpoint requests
    /// </summary>
    private async Task<IActionResult> HandleCompositeRequest(
        string env,
        string endpointName,
        string requestBody)
    {
        try
        {
            return await _compositeRequestHandler.HandleAsync(HttpContext, env, endpointName, requestBody);
        }
        catch (Exception ex)
        {
            return HandleUnexpectedError(ex, "composite (proxy)", endpointName,
                $"Error processing endpoint {endpointName}. Please check the logs for more details.");
        }
    }

}
