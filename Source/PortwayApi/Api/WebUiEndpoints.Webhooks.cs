namespace PortwayApi.Endpoints;

using System.Data.Common;
using PortwayApi.Auth;
using PortwayApi.Classes;
using PortwayApi.Helpers;
using PortwayApi.Services;

public static partial class WebUiEndpointExtensions
{
    private static void MapWebhookRoutes(WebApplication app)
    {
        // administrator only, payloads can hold personal data
        app.MapGet("/_/api/webhooks/{**name}", async (string name, string? env, HttpContext context) =>
        {
            if (context.Items[SignedInRoleKey] as string != AdminUserRoles.Administrator)
                return Results.Json(new { error = "This action needs an administrator account" }, statusCode: 403);

            var key = name.EndsWith("/deliveries", StringComparison.OrdinalIgnoreCase) ? name[..^"/deliveries".Length] : null;
            if (key is null || !EndpointHandler.GetSqlWebhookEndpoints().TryGetValue(key, out var endpoint))
                return Results.Json(new { error = "Webhook endpoint not found" }, statusCode: 404);

            var environments = context.RequestServices.GetRequiredService<EnvironmentSettings>();
            if (string.IsNullOrEmpty(env) || !environments.IsEnvironmentAllowed(env) || !endpoint.AllowsEnvironment(env))
                return Results.Json(new { error = "Environment is not allowed for this endpoint" }, statusCode: 400);

            try
            {
                var deliveries = await context.RequestServices.GetRequiredService<WebhookRequestHandler>().LatestAsync(env, endpoint, context.RequestAborted);
                return Results.Json(new
                {
                    deliveries = deliveries.Select(d => new { id = d.Id, webhook_id = d.WebhookId, received_at = d.ReceivedAt, payload = d.Payload })
                });
            }
            catch (DbException dbEx) when (!context.RequestAborted.IsCancellationRequested)
            {
                Serilog.Log.Warning("Reading deliveries of webhook {Endpoint} in {Environment} failed ({Detail}): {Message}",
                    key, env, SqlErrorClassifier.DescribeForLog(dbEx), dbEx.Message);
                return Results.Json(new { error = "Deliveries could not be read" }, statusCode: 500);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not DbException)
            {
                Serilog.Log.Warning(ex, "Reading deliveries of webhook {Endpoint} in {Environment} failed", key, env);
                return Results.Json(new { error = "Deliveries could not be read" }, statusCode: 500);
            }
        }).ExcludeFromDescription();
    }
}
