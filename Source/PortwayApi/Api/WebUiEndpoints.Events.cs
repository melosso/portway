namespace PortwayApi.Endpoints;

using Serilog;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;


public static partial class WebUiEndpointExtensions
{
    private static void MapEventRoutes(WebApplication app)
    {
        app.MapGet("/ui/api/events", async (HttpContext context) =>
        {
            var response = context.Response;
            response.Headers.ContentType = "text/event-stream";
            response.Headers.CacheControl = "no-cache";
            response.Headers.Append("X-Accel-Buffering", "no"); // disable nginx buffering

            var broadcaster = app.Services.GetRequiredService<PortwayApi.Services.SseBroadcaster>();
            var healthService = app.Services.GetRequiredService<PortwayApi.Services.HealthCheckService>();
            var ct = context.RequestAborted;

            // Push current health state immediately so the client doesn't wait for the next scheduled refresh
            try
            {
                var report = await healthService.CheckHealthAsync(ct);
                await response.WriteAsync($"event: health\ndata: {{\"status\":\"{report.Status}\"}}\n\n", ct);
                await response.Body.FlushAsync(ct);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { Log.Warning(ex, "Failed to push initial health state to SSE client"); }

            try
            {
                await foreach (var msg in broadcaster.SubscribeAsync(ct))
                {
                    await response.WriteAsync(msg, ct);
                    await response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException)
            {
                Log.Debug("SSE client disconnected, stopped streaming events");
            }
        }).ExcludeFromDescription();

        // Endpoint CRUD

        // PATCH /ui/api/endpoints/{type}/{**name}, rename (move directory)
    }
}
