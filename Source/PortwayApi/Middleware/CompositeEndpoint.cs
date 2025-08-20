namespace PortwayApi.Middleware;

using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PortwayApi.Classes;
using Serilog;

public class CompositeEndpoint
{
    private readonly CompositeEndpointHandler _compositeHandler;
    private readonly EnvironmentSettings _environmentSettings;

    public CompositeEndpoint(
        CompositeEndpointHandler compositeHandler,
        EnvironmentSettings environmentSettings)
    {
        _compositeHandler = compositeHandler;
        _environmentSettings = environmentSettings;
    }

    public async Task<IResult> HandleCompositeRequest(
        HttpContext context,
        string env,
        string endpointName)
    {
        Log.Information("🧩 Received composite request: {Path} {Method}", 
            context.Request.Path, context.Request.Method);

        try
        {
            // Check environment
            if (!_environmentSettings.IsEnvironmentAllowed(env))
            {
                Log.Warning("❌ Environment '{Env}' is not in the allowed list.", env);
                return Results.BadRequest(new { error = $"Environment '{env}' is not allowed." });
            }

            // Read the request body
            string requestBody;
            using (var reader = new StreamReader(context.Request.Body))
            {
                requestBody = await reader.ReadToEndAsync();
            }
            
            // Process the composite endpoint
            return await _compositeHandler.ProcessCompositeEndpointAsync(context, env, endpointName, requestBody);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ Error processing composite endpoint: {Error}", ex.Message);
            return Results.Problem(
                detail: ex.Message,
                title: "Internal Server Error",
                statusCode: 500
            );
        }
    }
}

// Extension method for easier registration in Program.cs
public static class CompositeEndpointExtensions
{
    public static WebApplication MapCompositeEndpoint(this WebApplication app)
    {
        app.Map("/api/{env}/composite/{endpointName}", async (
            HttpContext context,
            string env,
            string endpointName,
            [FromServices] CompositeEndpointHandler compositeHandler,
            [FromServices] EnvironmentSettings environmentSettings) =>
        {
            var endpoint = new CompositeEndpoint(compositeHandler, environmentSettings);
            return await endpoint.HandleCompositeRequest(context, env, endpointName);
        });

        return app;
    }
}