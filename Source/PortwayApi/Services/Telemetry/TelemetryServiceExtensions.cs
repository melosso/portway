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

        // Always register PortwayMetrics so that CacheManager and other services can depend on
        // it unconditionally. When telemetry is disabled, the Meter counters are no-ops
        // (nothing is listening), but DI graph validation succeeds in all configurations
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

        // Tracing pushes spans to a collector, so only the Otlp provider carries it
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
