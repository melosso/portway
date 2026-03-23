using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace PortwayApi.Services.Telemetry;

public sealed record TelemetryOptions
{
    public bool    Enabled            { get; init; } = false;
    public string  OtlpEndpoint       { get; init; } = "http://localhost:4317";
    public string? ServiceName        { get; init; }
    /// <summary>Additional resource attributes, e.g. "deployment.environment=production,host.name=gw01".</summary>
    public string? ResourceAttributes { get; init; }
}

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
        // (nothing is listening), but DI graph validation succeeds in all configurations.
        services.AddSingleton<PortwayMetrics>();

        if (!options.Enabled)
            return services;

        var serviceName = options.ServiceName ?? PortwayTelemetry.ServiceName;
        var endpoint    = new Uri(options.OtlpEndpoint);

        services.AddOpenTelemetry()
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
            })
            .WithTracing(t => t
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddSqlClientInstrumentation()
                .AddSource(PortwayTelemetry.ServiceName)
                .AddOtlpExporter(o =>
                {
                    o.Endpoint = endpoint;
                    o.Protocol = OtlpExportProtocol.Grpc;
                }))
            .WithMetrics(m => m
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddMeter(PortwayTelemetry.MeterName)
                .AddOtlpExporter(o =>
                {
                    o.Endpoint = endpoint;
                    o.Protocol = OtlpExportProtocol.Grpc;
                }));

        return services;
    }
}
