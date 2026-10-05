namespace PortwayApi.Middleware;

using PortwayApi.Classes;
using PortwayApi.Services;
using PortwayApi.Services.Telemetry;

/// <summary>
/// Records request metrics for all non-health paths; UI and API tracked separately
/// </summary>
public static class RequestMetricsExtensions
{
    private static readonly HashSet<string> KnownMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "POST", "PUT", "PATCH", "DELETE", "MERGE", "QUERY", "HEAD", "OPTIONS"
    };

    public static WebApplication UsePortwayRequestMetrics(this WebApplication app)
    {
        var metricsService = app.Services.GetRequiredService<MetricsService>();
        var portwayMetrics = app.Services.GetRequiredService<PortwayMetrics>();
        var environments = app.Services.GetRequiredService<EnvironmentSettings>();
        var scrapePath = app.Services.GetRequiredService<TelemetryOptions>().ActiveMetricsPath;

        app.Use(async (context, next) =>
        {
            var startTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            var statusCode = 0;
            try
            {
                await next();
                statusCode = context.Response.StatusCode;
            }
            catch when (!context.RequestAborted.IsCancellationRequested)
            {
                statusCode = StatusCodes.Status500InternalServerError;
                throw;
            }
            finally
            {
                var path = context.Request.Path;
                if (statusCode != 0 &&
                    !path.StartsWithSegments("/health") && !path.StartsWithSegments("/scalar") &&
                    (scrapePath is null || !path.StartsWithSegments(scrapePath)))
                {
                    var duration = System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp);
                    var source = path.StartsWithSegments("/ui") ? "ui" : path.StartsWithSegments("/api") ? "api" : "other";
                    var (environment, endpoint, version) = source == "api"
                        ? Label(path.Value, environments.IsEnvironmentAllowed)
                        : ("", "", "");
                    var method = KnownMethods.Contains(context.Request.Method) ? context.Request.Method.ToUpperInvariant() : "OTHER";

                    metricsService.Record(statusCode, method, source, endpoint, environment, version, (int)Math.Min(duration.TotalMilliseconds, int.MaxValue));
                    portwayMetrics.RequestCompleted(method, statusCode, source, endpoint + EndpointVersion.Suffix(version.Length > 0 ? version : null), duration);
                }
            }
        });

        return app;
    }

    internal static (string Environment, string Endpoint, string Version) Label(string? path, Func<string, bool> isAllowedEnvironment)
    {
        var segments = path?.Split('/', StringSplitOptions.RemoveEmptyEntries) ?? [];
        if (segments.Length < 3 || !segments[0].Equals("api", StringComparison.OrdinalIgnoreCase))
            return ("", "", "");

        var environment = isAllowedEnvironment(segments[1]) ? segments[1] : "";
        var rest = segments[2..];
        string? key = null;

        if (rest.Length >= 2 && rest[0].Equals("composite", StringComparison.OrdinalIgnoreCase))
        {
            if (EndpointHandler.GetProxyEndpoints().TryGetValue(rest[1], out var composite) && composite.IsComposite)
                key = $"composite/{rest[1]}";
        }
        else if (TokenAuthMiddleware.FileEndpointIdentity(rest) is { } file)
        {
            if (EndpointHandler.GetFileEndpoints().ContainsKey(file["files/".Length..]))
                key = file;
        }
        else if (Api.EndpointController.ResolveEndpointIdentity(rest) is { } resolved)
        {
            key = resolved.Namespace.Length > 0 ? $"{resolved.Namespace}/{resolved.Name}" : resolved.Name;
        }

        if (key is null)
            return (environment, "", "");

        var (endpoint, version) = EndpointVersion.Split(key);
        return (environment, endpoint, version ?? "");
    }
}
