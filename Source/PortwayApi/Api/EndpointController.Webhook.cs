using Microsoft.AspNetCore.Mvc;

using PortwayApi.Classes;
using PortwayApi.Helpers;
using PortwayApi.Services;
using Serilog;

namespace PortwayApi.Api;

public sealed partial class EndpointController
{
    /// <summary>
    /// A verified id accepts only the marker its own verification set; an unverified id needs none
    /// </summary>
    internal static bool AcceptsVerification(WebhookRoute route, EndpointDefinition endpoint, object? marker) =>
        endpoint.Verification?.Keys.FirstOrDefault(k => k.Equals(route.Id, StringComparison.OrdinalIgnoreCase)) is not { } verifiedId
        || (marker is WebhookRoute verified && verified == route with { Id = verifiedId });

    /// <summary>
    /// Routes a webhook delivery to <see cref="WebhookRequestHandler"/> and shapes the response
    /// </summary>
    private async Task<IActionResult> HandleWebhookRequest(string env, string[] segments, string requestBody)
    {
        if (!WebhookRoute.TryParse(env, segments, HttpContext, out var route))
            return PortwayResults.NotFound("Webhook not found.");

        if (!TryResolveEndpoint(EndpointType.Webhook, route.EndpointKey, null, out var endpoint, out var resolveError,
            $"Endpoint '{route.EndpointKey}' is not configured properly."))
            return resolveError;

        if (!endpoint.AllowedColumns.Contains(route.Id, StringComparer.OrdinalIgnoreCase))
            return PortwayResults.NotFound("Webhook not found.");

        if (!AcceptsVerification(route, endpoint, HttpContext.Items[Middleware.TokenAuthMiddleware.VerifiedWebhookKey]))
            return PortwayResults.Unauthorized("Authentication required");

        if (!JsonBody.TryParse(requestBody, out _))
            return PortwayResults.BadRequest(JsonBody.InvalidMessage);

        try
        {
            var result = await _webhookRequestHandler.StoreAsync(route, endpoint, requestBody, HttpContext.RequestAborted);
            return result.Outcome switch
            {
                WebhookOutcome.Stored => PortwayResults.Created("Webhook stored.", result.Id),
                WebhookOutcome.UnknownId => PortwayResults.NotFound("Webhook not found."),
                _ => PortwayResults.BadRequest("Environment is not configured properly.")
            };
        }
        catch (Exception ex)
        {
            return HandleUnexpectedError(ex, "webhook", route.Id, "Error processing webhook. Please check the logs for more details.");
        }
    }
}
