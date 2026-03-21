using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace PortwayApi.Services.Telemetry;

public sealed class TelemetryOptions
{
    public bool   Enabled      { get; set; } = false;
    public string OtlpEndpoint { get; set; } = "http://localhost:4317";
    public string? ServiceName { get; set; }
    /// <summary>Additional resource attributes, e.g. "deployment.environment=production,host.name=gw01".</summary>
    public string? ResourceAttributes { get; set; }
}

public static class TelemetryServiceExtensions
{
    public static IServiceCollection AddPortwayTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        string assemblyVersion)
    {
        var options = configuration.GetSection("Telemetry").Get<TelemetryOptions>() ?? new();

        if (!options.Enabled)
            return services;

        services.AddSingleton<PortwayMetrics>();

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
