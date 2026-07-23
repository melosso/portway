namespace PortwayApi.Interfaces;

using Microsoft.AspNetCore.Mvc;

/// <summary>Handles requests for one endpoint type, decoupled from the controller</summary>
public interface IEndpointRequestHandler
{
    Task<IActionResult> HandleAsync(HttpContext context, string env, string endpointName, string requestBody);
}
