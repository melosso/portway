namespace PortwayApi.Services.Mcp;

public sealed record McpOptions
{
    public bool   Enabled                { get; init; } = false;
    public string Path                   { get; init; } = "/mcp";
    public bool   RequireAuthentication  { get; init; } = true;
    public bool   AppsEnabled            { get; init; } = true;
    /// <summary>Enables the chat/AI feature; credentials are configured via the setup wizard</summary>
    public bool   ChatEnabled            { get; init; } = false;
    /// <summary>Rows appended as $top when the LLM omits it; prevents unbounded table scans</summary>
    public int    DefaultPageSize        { get; init; } = 50;
    /// <summary>Maximum $top value allowed; clamped if the LLM requests more</summary>
    public int    MaxPageSize            { get; init; } = 200;
    /// <summary>Timeout in seconds for internal tool-execution HTTP calls; default 30</summary>
    public int    ToolTimeoutSeconds     { get; init; } = 30;
    /// <summary>Maximum characters kept from a single tool result before truncation</summary>
    public int    MaxToolResultChars     { get; init; } = 12_000;
}
