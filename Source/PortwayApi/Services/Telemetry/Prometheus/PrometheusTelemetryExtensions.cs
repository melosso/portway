namespace PortwayApi.Services.Telemetry.Prometheus;

using OpenTelemetry.Metrics;

public static class PrometheusTelemetryExtensions
{
    public static MeterProviderBuilder AddPrometheusMetricsExporter(this MeterProviderBuilder metrics)
        => metrics.AddPrometheusExporter();

    /// <summary>Maps the scrape endpoint when the Prometheus provider is active</summary>
    public static WebApplication MapPortwayPrometheusScraping(this WebApplication app)
    {
        var options = app.Configuration.GetSection("Telemetry").Get<TelemetryOptions>() ?? new();
        if (options.ActiveMetricsPath is not { } path)
            return app;

        // Guard against config drift between registration and map time; a missing
        // MeterProvider would otherwise turn a config mismatch into a startup crash
        if (app.Services.GetService<MeterProvider>() is null)
            return app;

        app.MapPrometheusScrapingEndpoint(path);
        return app;
    }
}
