namespace PortwayApi.Services.Telemetry;

/// <summary>Selects which telemetry backend Portway publishes to; one provider at a time</summary>
public enum TelemetryProvider
{
    None,
    Otlp,
    Prometheus
}
