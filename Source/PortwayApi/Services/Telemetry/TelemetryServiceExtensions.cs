using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace PortwayApi.Services.Telemetry;

public static class TelemetryServiceExtensions
{
    public static IServiceCollection AddPortwayTelemetry(this IServiceCollection services, string assemblyVersion)
    {
        services.AddSingleton<PortwayMetrics>();

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(
                serviceName:    PortwayTelemetry.ServiceName,
                serviceVersion: assemblyVersion))
            .WithTracing(t => t
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddSqlClientInstrumentation()
                .AddSource(PortwayTelemetry.ServiceName)
                .AddOtlpExporter())
            .WithMetrics(m => m
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddMeter(PortwayTelemetry.MeterName)
                .AddOtlpExporter());

        return services;
    }
}
