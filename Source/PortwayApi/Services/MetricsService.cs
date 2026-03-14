namespace PortwayApi.Services;

using System.Collections.Concurrent;
using System.Threading.Channels;

/// <summary>
/// Thread-safe in-memory ring buffer for HTTP request metrics.
/// Records status code, method, source (api/ui/other) and endpoint name per request.
/// Auto-prunes entries older than 31 days, capped at MaxEntries to bound memory.
/// </summary>
public sealed class MetricsService
{
    internal readonly record struct RequestEntry(DateTime Timestamp, int StatusCode, string Method, string Source, string Endpoint);

    private const int MaxEntries = 500_000;
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(31);

    private readonly ConcurrentQueue<RequestEntry> _entries = new();
    private int _count;
    private long _cacheHits;
    private long _cacheMisses;

    internal readonly Channel<RequestEntry> PersistenceChannel = Channel.CreateBounded<RequestEntry>(
        new BoundedChannelOptions(20_000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = false });

    /// <summary>Records a completed HTTP request.</summary>
    public void Record(int statusCode, string method, string source = "api", string endpoint = "")
    {
        var now = DateTime.UtcNow;
        var entry = new RequestEntry(now, statusCode, method, source, endpoint);
        _entries.Enqueue(entry);
        PersistenceChannel.Writer.TryWrite(entry);

        if (Interlocked.Increment(ref _count) > MaxEntries)
        {
            if (_entries.TryDequeue(out _))
                Interlocked.Decrement(ref _count);
        }

        while (_entries.TryPeek(out var oldest) && now - oldest.Timestamp > MaxAge)
        {
            if (_entries.TryDequeue(out _))
                Interlocked.Decrement(ref _count);
        }
    }

    public void RecordCacheHit()  => Interlocked.Increment(ref _cacheHits);
    public void RecordCacheMiss() => Interlocked.Increment(ref _cacheMisses);

    internal void Hydrate(IEnumerable<RequestEntry> entries)
    {
        foreach (var entry in entries)
        {
            _entries.Enqueue(entry);
            Interlocked.Increment(ref _count);
        }
    }

    public MetricsSnapshot GetSnapshot(string period)
    {
        var (bucketCount, bucketSize, labelFormat, periodSpan) = period switch
        {
            "7d"  => (7,  TimeSpan.FromDays(1),  "ddd",   TimeSpan.FromDays(7)),
            "30d" => (30, TimeSpan.FromDays(1),  "MMM d", TimeSpan.FromDays(30)),
            _     => (24, TimeSpan.FromHours(1), "HH:mm", TimeSpan.FromHours(24)),
        };

        var now    = DateTime.UtcNow;
        var cutoff = now - periodSpan;

        var apiBuckets = new long[bucketCount];
        var uiBuckets  = new long[bucketCount];
        var errorMap   = new Dictionary<string, long>();
        long total = 0, errors = 0, apiReqs = 0, uiReqs = 0;
        var endpointCounts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        foreach (var e in _entries)
        {
            if (e.Timestamp < cutoff) continue;
            total++;

            var age = now - e.Timestamp;
            var idx = bucketCount - 1 - (int)(age.Ticks / bucketSize.Ticks);
            if (idx >= 0 && idx < bucketCount)
            {
                if (e.Source == "ui") uiBuckets[idx]++;
                else apiBuckets[idx]++;
            }

            if (e.StatusCode >= 400)
            {
                errors++;
                var key = e.StatusCode.ToString();
                errorMap.TryGetValue(key, out var cnt);
                errorMap[key] = cnt + 1;
            }

            if (e.Source == "ui") uiReqs++;
            else
            {
                apiReqs++;
                if (!string.IsNullOrEmpty(e.Endpoint))
                {
                    endpointCounts.TryGetValue(e.Endpoint, out var epCnt);
                    endpointCounts[e.Endpoint] = epCnt + 1;
                }
            }
        }

        var apiTraffic = new List<TrafficBucket>(bucketCount);
        var uiTraffic  = new List<TrafficBucket>(bucketCount);
        for (var i = 0; i < bucketCount; i++)
        {
            var bucketStart = now - periodSpan + TimeSpan.FromTicks(bucketSize.Ticks * i);
            var label = bucketStart.ToString(labelFormat);
            var ts    = bucketStart.ToString("yyyy-MM-ddTHH:mm:ssZ");
            apiTraffic.Add(new TrafficBucket(label, ts, apiBuckets[i]));
            uiTraffic.Add(new TrafficBucket(label, ts, uiBuckets[i]));
        }

        var topEndpoints = endpointCounts
            .OrderByDescending(kv => kv.Value)
            .Take(5)
            .Select(kv => new EndpointStat(kv.Key, kv.Value))
            .ToList();

        var errorRate  = total > 0 ? Math.Round((double)errors / total, 4) : 0.0;
        var startedAgo = (long)(now - _startTime).TotalSeconds;
        var hits       = Interlocked.Read(ref _cacheHits);
        var misses     = Interlocked.Read(ref _cacheMisses);

        return new MetricsSnapshot(period, apiTraffic, uiTraffic, errorMap, total, errorRate,
            startedAgo, apiReqs, uiReqs, topEndpoints, hits, misses);
    }

    private readonly DateTime _startTime = DateTime.UtcNow;
}

public sealed record TrafficBucket(string Label, string Timestamp, long Count);

public sealed record EndpointStat(string Name, long Count);

public sealed record MetricsSnapshot(
    string Period,
    List<TrafficBucket> ApiTraffic,
    List<TrafficBucket> UiTraffic,
    Dictionary<string, long> Errors,
    long Total,
    double ErrorRate,
    long CollectingForSeconds,
    long ApiRequests,
    long UiRequests,
    List<EndpointStat> TopEndpoints,
    long CacheHits,
    long CacheMisses
);
