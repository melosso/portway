namespace PortwayApi.Services.Telemetry.Prometheus;

public sealed record PrometheusOptions
{
    /// <summary>Route the scrape endpoint is served on</summary>
    public string Path { get; init; } = "/metrics";
}
