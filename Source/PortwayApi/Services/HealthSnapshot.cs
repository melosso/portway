namespace PortwayApi.Services;

public sealed record HealthSnapshot(
    HealthSummary Summary,
    List<LatencyBin> Latency,
    List<HealthRow> Breakdown,
    int BreakdownTotal,
    HealthOptions Options
);

public sealed record HealthSummary(
    long Total,
    long Failures,
    long ClientErrors,
    double? SuccessRate,
    int? MeanMs,
    int? P50,
    int? P90,
    int? P95,
    int? P99
);

public sealed record LatencyBin(int? UpperMs, long Count);

public sealed record HealthRow(string Environment, string Endpoint, string Version, string Method, HealthSummary Summary);

public sealed record HealthOptions(SortedSet<string> Environments, SortedSet<string> Endpoints, SortedSet<string> Versions, SortedSet<string> Methods);

public sealed record HealthFilter(string? Environment = null, string? Endpoint = null, string? Version = null, string? Method = null)
{
    internal bool Matches(MetricsService.RequestEntry e) =>
        (Environment is null || string.Equals(e.Environment, Environment, StringComparison.OrdinalIgnoreCase)) &&
        (Endpoint is null || string.Equals(e.Endpoint, Endpoint, StringComparison.OrdinalIgnoreCase)) &&
        (Version is null || string.Equals(e.Version, Version, StringComparison.OrdinalIgnoreCase)) &&
        (Method is null || string.Equals(e.Method, Method, StringComparison.OrdinalIgnoreCase));
}
