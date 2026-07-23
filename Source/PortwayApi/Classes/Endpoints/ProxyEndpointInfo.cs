namespace PortwayApi.Classes;

/// <summary>Snapshot of a proxy endpoint used by composite handling, MCP registration and summary logging</summary>
public sealed record ProxyEndpointInfo(
    string Url,
    HashSet<string> Methods,
    bool IsPrivate,
    bool IsMcpExposed,
    string Type,
    List<string>? AllowedEnvironments,
    List<string>? FallbackUrls = null,
    ProxyRetryOptions? Retry = null,
    ProxyResponseTransforms? ResponseTransforms = null);
