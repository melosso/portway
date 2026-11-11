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

        var rule = endpoint.Verification?.GetValueOrDefault(route.Id);
        var isForm = Microsoft.Net.Http.Headers.MediaTypeHeaderValue.TryParse(Request.ContentType, out var media)
            && media.MediaType.Equals("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase);
        if (isForm && rule is not { AcceptForm: true })
            return PortwayResults.UnsupportedMediaType("Form bodies are not accepted for this webhook id.");

        if (!isForm)
        {
            if (!JsonBody.TryParse(requestBody, out var json))
                return PortwayResults.BadRequest(JsonBody.InvalidMessage);

            // slack confirms the url with a signed challenge
            if (rule?.Scheme == "Slack" && json.ValueKind == System.Text.Json.JsonValueKind.Object
                && json.TryGetProperty("type", out var type) && type.ValueEquals("url_verification")
                && json.TryGetProperty("challenge", out var challenge) && challenge.ValueKind == System.Text.Json.JsonValueKind.String)
                return Ok(new { challenge = challenge.GetString() });
        }

        try
        {
            var result = await _webhookRequestHandler.StoreAsync(route, endpoint, requestBody, Request.Headers, HttpContext.RequestAborted);
            var metrics = HttpContext.RequestServices.GetService<Services.Telemetry.PortwayMetrics>();
            
            if (result.Outcome == WebhookOutcome.Stored)
                metrics?.WebhookStored(route.EndpointKey, rule?.Scheme);
            else if (result.Outcome == WebhookOutcome.Duplicate)
                metrics?.WebhookDuplicate(route.EndpointKey, rule?.Scheme);
            
            return result.Outcome switch
            {
                WebhookOutcome.Stored => PortwayResults.Created("Webhook stored.", result.Id),
                WebhookOutcome.UnknownId => PortwayResults.NotFound("Webhook not found."),
                WebhookOutcome.Duplicate => PortwayResults.Mutation("Duplicate delivery ignored."),
                WebhookOutcome.InvalidDeliveryId => PortwayResults.BadRequest($"Delivery id is missing or longer than {WebhookRequestHandler.MaxDeliveryIdLength} characters."),
                _ => PortwayResults.BadRequest("Environment is not configured properly.")
            };
        }
        catch (Exception ex)
        {
            return HandleUnexpectedError(ex, "webhook", route.Id, "Error processing webhook. Please check the logs for more details.");
        }
    }
}
