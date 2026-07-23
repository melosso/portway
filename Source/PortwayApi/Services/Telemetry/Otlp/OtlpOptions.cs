namespace PortwayApi.Services.Telemetry.Otlp;

public sealed record OtlpOptions
{
    /// <summary>Collector gRPC address; falls back to the legacy flat Telemetry:OtlpEndpoint key when unset</summary>
    public string? Endpoint { get; init; }
}
