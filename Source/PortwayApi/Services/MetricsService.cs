namespace PortwayApi.Services;

using System.Collections.Concurrent;
using System.Threading.Channels;

/// <summary>
/// Thread-safe in-memory ring buffer for HTTP request metrics. Records status code, method, source (api/ui/other) and endpoint name per request. Auto-prunes entries older than 31 days, capped at MaxEntries to bound memory
/// </summary>
public sealed class MetricsService
{
    internal readonly record struct RequestEntry(DateTime Timestamp, int StatusCode, string Method, string Source, string Endpoint,
        string Environment = "", string Version = "", int? DurationMs = null);

    private const int MaxEntries = 500_000;
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(31);

    private readonly ConcurrentQueue<RequestEntry> _entries = new();
    private int _count;
    private long _cacheHits;
    private long _cacheMisses;
    private long _clearedAtTicks;

    internal DateTime ClearedAt => new(Interlocked.Read(ref _clearedAtTicks), DateTimeKind.Utc);

    internal readonly Channel<RequestEntry> PersistenceChannel = Channel.CreateBounded<RequestEntry>(
        new BoundedChannelOptions(20_000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = false });

    /// <summary>
    /// Records a completed HTTP request.
    /// </summary>
    public void Record(int statusCode, string method, string source = "api", string endpoint = "",
        string environment = "", string version = "", int? durationMs = null)
    {
        var now = DateTime.UtcNow;
        var entry = new RequestEntry(now, statusCode, method, source, endpoint, environment, version, durationMs);
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

    public void RecordCacheHit() => Interlocked.Increment(ref _cacheHits);
    public void RecordCacheMiss() => Interlocked.Increment(ref _cacheMisses);

    public DateTime Clear()
    {
        var clearedAt = DateTime.UtcNow;
        Interlocked.Exchange(ref _clearedAtTicks, clearedAt.Ticks);
        while (_entries.TryPeek(out var oldest) && oldest.Timestamp < clearedAt && _entries.TryDequeue(out _))
            Interlocked.Decrement(ref _count);
        return clearedAt;
    }

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
            "7d" => (7, TimeSpan.FromDays(1), "ddd", TimeSpan.FromDays(7)),
            "30d" => (30, TimeSpan.FromDays(1), "MMM d", TimeSpan.FromDays(30)),
            _ => (24, TimeSpan.FromHours(1), "HH:mm", TimeSpan.FromHours(24)),
        };

        var now = DateTime.UtcNow;
        var cutoff = Max(now - periodSpan, ClearedAt);

        var apiBuckets = new long[bucketCount];
        var uiBuckets = new long[bucketCount];
        var errorMap = new Dictionary<string, long>();
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
                // Skip 404s so probes stay out of the top endpoints
                if (!string.IsNullOrEmpty(e.Endpoint) && e.StatusCode != 404)
                {
                    endpointCounts.TryGetValue(e.Endpoint, out var epCnt);
                    endpointCounts[e.Endpoint] = epCnt + 1;
                }
            }
        }

        var apiTraffic = new List<TrafficBucket>(bucketCount);
        var uiTraffic = new List<TrafficBucket>(bucketCount);
        for (var i = 0; i < bucketCount; i++)
        {
            var bucketStart = now - periodSpan + TimeSpan.FromTicks(bucketSize.Ticks * i);
            var label = bucketStart.ToString(labelFormat);
            var ts = bucketStart.ToString("yyyy-MM-ddTHH:mm:ssZ");
            apiTraffic.Add(new TrafficBucket(label, ts, apiBuckets[i]));
            uiTraffic.Add(new TrafficBucket(label, ts, uiBuckets[i]));
        }

        var topEndpoints = endpointCounts
            .OrderByDescending(kv => kv.Value)
            .Take(5)
            .Select(kv => new EndpointStat(kv.Key, kv.Value))
            .ToList();

        var errorRate = total > 0 ? Math.Round((double)errors / total, 4) : 0.0;
        var startedAgo = (long)(now - _startTime).TotalSeconds;
        var hits = Interlocked.Read(ref _cacheHits);
        var misses = Interlocked.Read(ref _cacheMisses);

        return new MetricsSnapshot(period, apiTraffic, uiTraffic, errorMap, total, errorRate,
            startedAgo, apiReqs, uiReqs, topEndpoints, hits, misses);
    }

    private static readonly int[] LatencyBinEdgesMs = [5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000];
    private const int BreakdownLimit = 500;

    public static TimeSpan? HealthPeriod(string? period) => period switch
    {
        "1h" => TimeSpan.FromHours(1),
        "24h" => TimeSpan.FromHours(24),
        "7d" => TimeSpan.FromDays(7),
        "30d" => TimeSpan.FromDays(30),
        _ => null
    };

    public HealthSnapshot GetHealth(TimeSpan period, HealthFilter filter)
    {
        var cutoff = Max(DateTime.UtcNow - period, ClearedAt);
        var options = new HealthOptions([], [], [], []);
        var matched = new List<RequestEntry>();

        foreach (var e in _entries)
        {
            if (e.Timestamp < cutoff || e.Source != "api") continue;
            options.Environments.Add(e.Environment);
            options.Endpoints.Add(e.Endpoint);
            options.Versions.Add(e.Version);
            options.Methods.Add(e.Method);
            if (filter.Matches(e)) matched.Add(e);
        }

        var overall = Summarise(matched);
        var bins = new long[LatencyBinEdgesMs.Length + 1];
        foreach (var e in matched)
        {
            if (e.DurationMs is not { } ms) continue;
            var i = Array.FindIndex(LatencyBinEdgesMs, edge => ms <= edge);
            bins[i < 0 ? LatencyBinEdgesMs.Length : i]++;
        }

        var groups = matched
            .GroupBy(e => (e.Environment, e.Endpoint, e.Version, e.Method))
            .Select(g => new HealthRow(g.Key.Environment, g.Key.Endpoint, g.Key.Version, g.Key.Method, Summarise(g.ToList())))
            .OrderByDescending(r => r.Summary.Failures)
            .ThenByDescending(r => r.Summary.P95 ?? -1)
            .ThenByDescending(r => r.Summary.Total)
            .ToList();

        return new HealthSnapshot(
            overall,
            [.. bins.Select((count, i) => new LatencyBin(i < LatencyBinEdgesMs.Length ? LatencyBinEdgesMs[i] : null, count))],
            [.. groups.Take(BreakdownLimit)],
            groups.Count,
            options);
    }

    internal static HealthSummary Summarise(IReadOnlyCollection<RequestEntry> entries)
    {
        long failures = 0, clientErrors = 0;
        var durations = new List<int>(entries.Count);
        foreach (var e in entries)
        {
            if (e.StatusCode >= 500) failures++;
            else if (e.StatusCode >= 400) clientErrors++;
            if (e.DurationMs is { } ms) durations.Add(ms);
        }

        durations.Sort();
        int? Percentile(double p) => durations.Count == 0 ? null : durations[(int)Math.Ceiling(p / 100 * durations.Count) - 1];
        int total = entries.Count;

        return new HealthSummary(
            total,
            failures,
            clientErrors,
            total > 0 ? Math.Round((double)(total - failures) / total, 4) : null,
            durations.Count > 0 ? (int)Math.Round(durations.Average()) : null,
            Percentile(50), Percentile(90), Percentile(95), Percentile(99));
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    private readonly DateTime _startTime = DateTime.UtcNow;
}
