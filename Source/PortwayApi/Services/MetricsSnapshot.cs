namespace PortwayApi.Services;

using System.Collections.Concurrent;
using System.Threading.Channels;

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
