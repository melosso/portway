namespace PortwayApi.Services.Mcp;

using ModelContextProtocol.Server;
using System.ComponentModel;

public sealed record UiEnabledEndpointsResult(
    int Count,
    IReadOnlyList<UiEndpointItem> Endpoints
);
