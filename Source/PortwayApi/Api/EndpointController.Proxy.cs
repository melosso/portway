using Microsoft.AspNetCore.Mvc;
using PortwayApi.Classes;
using PortwayApi.Services;

namespace PortwayApi.Api;

public partial class EndpointController
{
    /// <summary>Handles proxy requests for any HTTP method</summary>
    private async Task<IActionResult> HandleProxyRequest(
        string env,
        string endpointName,
        string? id,
        string remainingPath,
        string method)
    {
        if (TryResolveEndpoint(EndpointType.Proxy, endpointName, null, out var endpointDefinition) is { } resolveError)
        {
            return resolveError;
        }

        return await _proxyRequestHandler.HandleProxyRequest(HttpContext, endpointDefinition, env, endpointName, id, remainingPath, method);
    }
}
