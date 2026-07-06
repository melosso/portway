namespace PortwayApi.Services.Mcp;

using ModelContextProtocol.Server;
using System.ComponentModel;

// Named response types; avoid returning untyped object from MCP tool methods
public sealed record EndpointInfoResult
{
    public string? Error { get; init; }
    public string? Name { get; init; }
    public string? Ns { get; init; }
    public string? Method { get; init; }
    public string? Url { get; init; }
    public IReadOnlyList<string> AllowedEnvironments { get; init; } = [];
    public bool HasUi { get; init; }
    public string? UiUri { get; init; }
}
