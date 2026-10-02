namespace PortwayApi.Services.Telemetry.Prometheus;

using OpenTelemetry.Metrics;

public static class PrometheusTelemetryExtensions
{
    public static MeterProviderBuilder AddPrometheusMetricsExporter(this MeterProviderBuilder metrics)
        => metrics.AddPrometheusExporter();

    /// <summary>
    /// Maps the scrape endpoint when the Prometheus provider is active
    /// </summary>
    public static WebApplication MapPortwayPrometheusScraping(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<TelemetryOptions>();
        if (options.ActiveMetricsPath is not { } path)
            return app;

        // Missing MeterProvider means config drift, skip instead of crashing
        if (app.Services.GetService<MeterProvider>() is null)
            return app;

        app.MapPrometheusScrapingEndpoint(path);
        return app;
    }
}
