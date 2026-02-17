using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace PortwayApi.Api;

/// <summary>
/// Controller to provide OpenAPI metadata only, not for actual request handling
/// </summary>
[ApiController]
[Route("api/openapi-docs")]
[ApiExplorerSettings(IgnoreApi = true)]
public class OpenApiDocsController : ControllerBase
{
    /// <summary>
    /// This controller doesn't actually handle requests, but only exists to provide properly formatted OpenAPI metadata for the API explorer.
    /// The real implementation is in EndpointController, but this helps populate the API structure.
    /// </summary>
    [HttpGet]
    [Route("info")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public IActionResult GetApiInfo()
    {
        // Just return the basic info, this endpoint is only for documentation
        return Ok(new {
            message = "This is a documentation-only endpoint. Use the actual endpoints for API calls.",
            endpoints = new {
                sql = "/api/{env}/{endpointName}",
                proxy = "/api/{env}/{endpointName}",
                webhook = "/api/{env}/webhook/{webhookId}",
                composite = "/api/{env}/composite/{endpointName}",
                file = "/api/{env}/files/{endpointName}/{fileId}"
            }
        });
    }
}
