namespace PortwayApi.Services;

using System.Collections.Concurrent;
using System.Threading.Channels;

public sealed record EndpointStat(string Name, long Count);
