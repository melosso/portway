namespace PortwayApi.Services.Mcp;

public sealed record EndpointMcpInfo
{
    public string                  Name                { get; init; } = string.Empty;
    public string?                 Namespace           { get; init; }
    public string                  Url                 { get; init; } = string.Empty;
    public IReadOnlyList<string>   Methods             { get; init; } = [];
    public IReadOnlyList<string>?  AllowedEnvironments { get; init; }
    public IReadOnlyList<string>?  AvailableFields     { get; init; }
    /// <summary>Human-readable per-method summaries from Documentation.MethodDescriptions</summary>
    public Dictionary<string, string>? MethodDescriptions { get; init; }
    /// <summary>Endpoint-level description from Documentation.TagDescription or Documentation.Description</summary>
    public string? Description { get; init; }
    /// <summary>Usage instruction appended to the tool description (from Mcp.Instruction)</summary>
    public string? Instruction { get; init; }
    public bool    UiEnabled    { get; init; } = false;
    /// <summary>MIME type for Static endpoints (e.g. "text/csv"); null for SQL/Proxy</summary>
    public string? ContentType  { get; init; }
    /// <summary>"api" | "static" | "file"; affects how the chat layer handles the response</summary>
    public string  EndpointKind { get; init; } = "api";
}
