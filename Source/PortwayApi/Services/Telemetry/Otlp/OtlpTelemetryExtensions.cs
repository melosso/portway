namespace PortwayApi.Services.Telemetry.Otlp;

using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

public static class OtlpTelemetryExtensions
{
    public static OpenTelemetryBuilder WithOtlpTracing(this OpenTelemetryBuilder otel, TelemetryOptions options)
    {
        var endpoint = new Uri(options.EffectiveOtlpEndpoint);

        return otel.WithTracing(t => t
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddSqlClientInstrumentation()
            .AddSource(PortwayTelemetry.ServiceName)
            .AddOtlpExporter(o =>
            {
                o.Endpoint = endpoint;
                o.Protocol = OtlpExportProtocol.Grpc;
            }));
    }

    public static MeterProviderBuilder AddOtlpMetricsExporter(this MeterProviderBuilder metrics, TelemetryOptions options)
    {
        return metrics.AddOtlpExporter(o =>
        {
            o.Endpoint = new Uri(options.EffectiveOtlpEndpoint);
            o.Protocol = OtlpExportProtocol.Grpc;
        });
    }
}
