namespace PortwayApi.Services;

using Microsoft.AspNetCore.Mvc;
using PortwayApi.Classes;
using PortwayApi.Helpers;
using PortwayApi.Interfaces;
using Serilog;

/// <summary>Executes composite endpoint requests outside the controller</summary>
public class CompositeRequestHandler : IEndpointRequestHandler
{
    private readonly CompositeEndpointHandler _compositeHandler;

    public CompositeRequestHandler(CompositeEndpointHandler compositeHandler)
    {
        _compositeHandler = compositeHandler;
    }

    public async Task<IActionResult> HandleAsync(HttpContext context, string env, string endpointName, string requestBody)
    {
        Log.Debug("Processing composite endpoint: {Endpoint}", endpointName);

        string compositeName = endpointName;
        if (endpointName.StartsWith("composite/", StringComparison.OrdinalIgnoreCase))
        {
            compositeName = endpointName.Substring("composite/".Length);
        }

        var result = await _compositeHandler.ProcessCompositeEndpointAsync(
            context, env, compositeName, requestBody);

        // Convert from IResult to IActionResult
        if (result is IStatusCodeHttpResult statusCodeResult)
        {
            var statusCode = statusCodeResult.StatusCode ?? 200;
            var value = (result is IValueHttpResult valueResult) ? valueResult.Value : null;

            return new ObjectResult(value) { StatusCode = statusCode };
        }

        if (result is ObjectResult objectResult)
        {
            return new ObjectResult(objectResult.Value) { StatusCode = objectResult.StatusCode ?? 500 };
        }

        return new OkObjectResult(MutationResponse.Of("Record(s) created successfully"));
    }
}
