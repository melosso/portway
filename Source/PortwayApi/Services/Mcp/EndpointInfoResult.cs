namespace PortwayApi.Services.Mcp;
// Named response types; avoid returning untyped object from MCP tool methods
public sealed record EndpointInfoResult
{
    public string? Error { get; init; }
    // pass this to CallEndpoint
    public string? InvokeName { get; init; }
    public string? Name { get; init; }
    public string? Ns { get; init; }
    public string? Method { get; init; }
    public string? Url { get; init; }
    public IReadOnlyList<string> AllowedEnvironments { get; init; } = [];
    public IReadOnlyList<string> TenantHeaders { get; init; } = [];
}

/// <summary>
/// Text of a tool call and whether the call failed
/// </summary>
internal readonly record struct McpToolResult(string Text, bool IsError = false)
{
    public static McpToolResult Failure(string text) => new(text, true);
}
