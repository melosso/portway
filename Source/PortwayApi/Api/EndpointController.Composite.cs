using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using System.Data.Common;
using PortwayApi.Services.Providers;

using Dapper;
using System.Data;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Xml.Linq;
using PortwayApi.Classes;
using PortwayApi.Helpers;
using PortwayApi.Interfaces;
using PortwayApi.Services;
using PortwayApi.Services.Files;
using Serilog;
using System.Runtime.CompilerServices;

namespace PortwayApi.Api;

public partial class EndpointController
{
    /// <summary>Handles composite endpoint requests</summary>
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
            Log.Error(ex, "Error during composite (proxy) request: {EndpointName}", endpointName);
            
            return Problem(
                detail: $"Error processing endpoint {endpointName}. Please check the logs for more details.",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Error"
            );
        }
    }

}
