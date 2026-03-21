using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PortwayApi.Services.Telemetry;

public sealed class PortwayMetrics : IDisposable
{
    private readonly Meter _meter;
    private readonly Counter<long>     _cacheHitCounter;
    private readonly Counter<long>     _cacheMissCounter;
    private readonly Histogram<double> _requestDuration;

    public PortwayMetrics()
    {
        _meter = new Meter(PortwayTelemetry.MeterName, "0.1.0");

        _cacheHitCounter = _meter.CreateCounter<long>(
            "portway.cache.hit.count",
            unit: "{hit}",
            description: "Number of cache hits served");

        _cacheMissCounter = _meter.CreateCounter<long>(
            "portway.cache.miss.count",
            unit: "{miss}",
            description: "Number of cache misses");

        _requestDuration = _meter.CreateHistogram<double>(
            "portway.request.duration",
            unit: "s",
            description: "Duration of API requests");
    }

    public void CacheHit()  => _cacheHitCounter.Add(1);
    public void CacheMiss() => _cacheMissCounter.Add(1);

    public void RequestCompleted(string method, int statusCode, string source, TimeSpan duration)
    {
        var tags = new TagList
        {
            { "http.method",                method },
            { "http.response.status_code",  statusCode },
            { "portway.request_source",     source }   // "api" | "ui" | "other"
        };
        _requestDuration.Record(duration.TotalSeconds, tags);
    }

    public void Dispose() => _meter.Dispose();
}
