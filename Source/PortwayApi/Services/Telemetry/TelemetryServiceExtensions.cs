namespace PortwayApi.Services.Telemetry;

using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using PortwayApi.Services.Telemetry.Otlp;
using PortwayApi.Services.Telemetry.Prometheus;

public static class TelemetryServiceExtensions
{
    public static IServiceCollection AddPortwayTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        string assemblyVersion)
    {
        var options = configuration.GetSection("Telemetry").Get<TelemetryOptions>() ?? new();

        // Single bound instance for middlewares and endpoints; avoids repeated config binds and registration-vs-map-time drift
        services.AddSingleton(options);

        // Always registered so dependents resolve; counters are no-ops when telemetry is off
        services.AddSingleton<PortwayMetrics>();

        var provider = options.EffectiveProvider;
        if (provider == TelemetryProvider.None)
            return services;

        var serviceName = options.ServiceName ?? PortwayTelemetry.ServiceName;

        var otel = services.AddOpenTelemetry()
            .ConfigureResource(r =>
            {
                r.AddService(serviceName, serviceVersion: assemblyVersion);

                if (!string.IsNullOrWhiteSpace(options.ResourceAttributes))
                {
                    var attrs = options.ResourceAttributes
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(pair => pair.Split('=', 2))
                        .Where(parts => parts.Length == 2)
                        .Select(parts => new KeyValuePair<string, object>(parts[0].Trim(), parts[1].Trim()));

                    r.AddAttributes(attrs);
                }
            });

        // Tracing needs a collector to push spans to; only the Otlp provider has one configured
        if (provider == TelemetryProvider.Otlp)
            otel.WithOtlpTracing(options);

        otel.WithMetrics(m =>
        {
            m.AddAspNetCoreInstrumentation()
             .AddHttpClientInstrumentation()
             .AddMeter(PortwayTelemetry.MeterName);

            switch (provider)
            {
                case TelemetryProvider.Otlp:
                    m.AddOtlpMetricsExporter(options);
                    break;
                case TelemetryProvider.Prometheus:
                    m.AddPrometheusMetricsExporter();
                    break;
            }
        });

        return services;
    }
}
