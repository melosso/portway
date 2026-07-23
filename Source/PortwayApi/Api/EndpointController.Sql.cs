using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using PortwayApi.Classes;
using PortwayApi.Services;

namespace PortwayApi.Api;

public partial class EndpointController
{
    private async Task<IActionResult> HandleSqlGetRequest(
        string env,
        string endpointName,
        string? id,
        string? remainingPath,
        string? select,
        string? filter,
        string? orderby,
        int top,
        int skip,
        string httpMethod = "GET")
    {
        if (TryResolveEndpoint(EndpointType.SQL, endpointName, null, out var endpoint) is { } resolveError)
        {
            return resolveError;
        }

        return await _sqlRequestHandler.HandleSqlGetRequest(HttpContext, endpoint, env, endpointName, id, remainingPath, select, filter, orderby, top, skip, httpMethod);
    }

    private async Task<IActionResult> HandleSqlPostRequest(
        string env,
        string endpointName,
        JsonElement data)
    {
        if (TryResolveEndpoint(EndpointType.SQL, endpointName, null, out var endpoint) is { } resolveError)
        {
            return resolveError;
        }

        return await _sqlRequestHandler.HandleSqlPostRequest(HttpContext, endpoint, env, endpointName, data);
    }

    private async Task<IActionResult> HandleSqlPutRequest(
        string env,
        string endpointName,
        JsonElement data)
    {
        if (TryResolveEndpoint(EndpointType.SQL, endpointName, null, out var endpoint) is { } resolveError)
        {
            return resolveError;
        }

        return await _sqlRequestHandler.HandleSqlPutRequest(HttpContext, endpoint, env, endpointName, data);
    }

    private async Task<IActionResult> HandleSqlPatchRequest(
        string env,
        string endpointName,
        JsonDocument requestBody)
    {
        if (TryResolveEndpoint(EndpointType.SQL, endpointName, null, out var endpoint) is { } resolveError)
        {
            return resolveError;
        }

        return await _sqlRequestHandler.HandleSqlPatchRequest(HttpContext, endpoint, env, endpointName, requestBody);
    }

    private async Task<IActionResult> HandleSqlDeleteRequest(
        string env,
        string endpointName,
        string id)
    {
        if (TryResolveEndpoint(EndpointType.SQL, endpointName, null, out var endpoint) is { } resolveError)
        {
            return resolveError;
        }

        return await _sqlRequestHandler.HandleSqlDeleteRequest(HttpContext, endpoint, env, endpointName, id);
    }
}
