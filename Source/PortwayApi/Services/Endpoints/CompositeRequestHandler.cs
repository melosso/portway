namespace PortwayApi.Services;

using Microsoft.AspNetCore.Mvc;
using PortwayApi.Classes;
using Serilog;

/// <summary>
/// Executes composite endpoint requests outside the controller
/// </summary>
public sealed class CompositeRequestHandler
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

        return new HttpResultAction(result);
    }

    private sealed class HttpResultAction(IResult result) : IActionResult
    {
        public Task ExecuteResultAsync(ActionContext context) => result.ExecuteAsync(context.HttpContext);
    }
}
