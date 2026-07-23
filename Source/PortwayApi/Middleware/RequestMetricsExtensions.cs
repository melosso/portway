namespace PortwayApi.Middleware;

using PortwayApi.Services;
using PortwayApi.Services.Telemetry;

/// <summary>Records request metrics for all non-health paths; UI and API tracked separately</summary>
public static class RequestMetricsExtensions
{
    public static WebApplication UsePortwayRequestMetrics(this WebApplication app)
    {
        var metricsService = app.Services.GetRequiredService<MetricsService>();
        var portwayMetrics = app.Services.GetRequiredService<PortwayMetrics>();
        var scrapePath     = app.Services.GetRequiredService<TelemetryOptions>().ActiveMetricsPath;

        app.Use(async (context, next) =>
        {
            var startTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            await next();
            var duration = System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp);

            var path = context.Request.Path;
            if (path.StartsWithSegments("/health") || path.StartsWithSegments("/scalar")) return;
            if (scrapePath is not null && path.StartsWithSegments(scrapePath)) return;
            string source, endpoint;
            if (path.StartsWithSegments("/ui"))
            {
                source = "ui"; endpoint = "";
            }
            else
            {
                source   = "api";
                endpoint = ParseEndpointName(path.Value);
            }
            metricsService.Record(context.Response.StatusCode, context.Request.Method, source, endpoint);
            portwayMetrics.RequestCompleted(context.Request.Method, context.Response.StatusCode, source, endpoint, duration);
        });

        return app;
    }

    /// <summary>Parses "/api/{env}/{name}" or "/webhook/{env}/{name}" to "{name}"; composite paths yield "composite/{name}"</summary>
    internal static string ParseEndpointName(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        var segs = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segs.Length < 3) return "";
        var prefix = segs[0].ToLowerInvariant();
        if (prefix != "api" && prefix != "webhook") return "";
        // segs[1] = env, segs[2] = name (or "composite"), segs[3] = composite name
        if (segs.Length >= 4 && segs[2].Equals("composite", StringComparison.OrdinalIgnoreCase))
            return $"composite/{segs[3]}";
        return segs[2];
    }
}
