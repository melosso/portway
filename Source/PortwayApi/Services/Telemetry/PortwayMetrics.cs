using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PortwayApi.Services.Telemetry;

public sealed class PortwayMetrics : IDisposable
{
    private readonly Meter _meter;
    private readonly Counter<long> _cacheHitCounter;
    private readonly Counter<long> _cacheMissCounter;
    private readonly Histogram<double> _requestDuration;
    private readonly Counter<long> _webhookStored;
    private readonly Counter<long> _webhookDuplicate;
    private readonly Counter<long> _webhookVerificationFailed;

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

        _webhookStored = _meter.CreateCounter<long>("portway.webhook.stored.count", unit: "{delivery}", description: "Webhook deliveries stored");
        _webhookDuplicate = _meter.CreateCounter<long>("portway.webhook.duplicate.count", unit: "{delivery}", description: "Duplicate webhook deliveries ignored");
        _webhookVerificationFailed = _meter.CreateCounter<long>("portway.webhook.verification_failed.count", unit: "{delivery}", description: "Webhook deliveries refused by verification");
    }

    // endpoint and scheme only, never payload or header values
    private static TagList WebhookTags(string endpoint, string? scheme) => new() { { "portway.endpoint", endpoint }, { "portway.webhook.scheme", scheme ?? "Bearer" } };

    public void WebhookStored(string endpoint, string? scheme) => _webhookStored.Add(1, WebhookTags(endpoint, scheme));
    public void WebhookDuplicate(string endpoint, string? scheme) => _webhookDuplicate.Add(1, WebhookTags(endpoint, scheme));
    public void WebhookVerificationFailed(string endpoint, string? scheme) => _webhookVerificationFailed.Add(1, WebhookTags(endpoint, scheme));

    public void CacheHit() => _cacheHitCounter.Add(1);
    public void CacheMiss() => _cacheMissCounter.Add(1);

    public void RequestCompleted(string method, int statusCode, string source, string endpoint, TimeSpan duration)
    {
        var tags = new TagList
        {
            { "http.method",                method },
            { "http.response.status_code",  statusCode },
            { "portway.request_source",     source },  // "api" | "ui" | "other"
            { "portway.endpoint",           endpoint } // configured endpoint name, "" for non-endpoint paths
        };
        _requestDuration.Record(duration.TotalSeconds, tags);
    }

    public void Dispose() => _meter.Dispose();
}
