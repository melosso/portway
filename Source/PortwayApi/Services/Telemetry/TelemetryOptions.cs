namespace PortwayApi.Services.Telemetry;

using PortwayApi.Services.Telemetry.Otlp;
using PortwayApi.Services.Telemetry.Prometheus;

public sealed record TelemetryOptions
{
    public TelemetryProvider Provider { get; init; } = TelemetryProvider.None;
    public string? ServiceName        { get; init; }
    /// <summary>Additional resource attributes, e.g. "deployment.environment=production,host.name=gw01".</summary>
    public string? ResourceAttributes { get; init; }

    public OtlpOptions       Otlp       { get; init; } = new();
    public PrometheusOptions Prometheus { get; init; } = new();

    /// <summary>Legacy flat switch; true selects the Otlp provider when Provider is unset</summary>
    public bool Enabled { get; init; } = false;
    /// <summary>Legacy flat endpoint; used when Otlp:Endpoint is not configured</summary>
    public string OtlpEndpoint { get; init; } = "http://localhost:4317";

    public TelemetryProvider EffectiveProvider =>
        Provider == TelemetryProvider.None && Enabled ? TelemetryProvider.Otlp : Provider;

    public string EffectiveOtlpEndpoint => Otlp.Endpoint ?? OtlpEndpoint;

    /// <summary>Scrape path when the Prometheus provider is active, otherwise null</summary>
    public string? ActiveMetricsPath =>
        EffectiveProvider == TelemetryProvider.Prometheus ? Prometheus.Path : null;
}
