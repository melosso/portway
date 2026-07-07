namespace PortwayApi.Services;

using System.Collections.Concurrent;
using System.Threading.Channels;

public sealed record TrafficBucket(string Label, string Timestamp, long Count);
