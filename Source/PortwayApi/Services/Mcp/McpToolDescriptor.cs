namespace PortwayApi.Services.Mcp;

using PortwayApi.Classes;

public sealed record McpToolDescriptor
{
    public string Name { get; init; } = string.Empty;
    /// <summary>
    /// LLM-facing description; includes method, fields and environment metadata
    /// </summary>
    public string Description { get; init; } = string.Empty;
    /// <summary>
    /// Human-facing display label shown in the chat UI, e.g. "Retrieve warehouse locations"
    /// </summary>
    public string DisplayDescription { get; init; } = string.Empty;
    public string EndpointName { get; init; } = string.Empty;
    public string? Namespace { get; init; }
    public string? Version { get; init; }

    private string BasePath => string.IsNullOrEmpty(Namespace) ? EndpointName : $"{Namespace}/{EndpointName}";

    /// <summary>
    /// Scope key the token middleware checks for the same endpoint
    /// </summary>
    public string Identity => (EndpointKind == "file" ? "files/" : "") + BasePath + EndpointVersion.Suffix(Version);

    /// <summary>
    /// Path below /api/{env} that serves this endpoint
    /// </summary>
    public string RoutePath => EndpointVersion.RoutePrefix(Version) + (EndpointKind == "file" ? "files/" : "") + BasePath;
    public string Method { get; init; } = string.Empty;
    public IReadOnlyList<string>? AllowedEnvironments { get; init; }
    public IReadOnlyList<string>? AvailableFields { get; init; }
    public IReadOnlyList<string> TenantHeaders { get; init; } = [];
    public string Url { get; init; } = string.Empty;
    /// <summary>
    /// MIME type for Static endpoints (e.g. "text/csv"); null for SQL/Proxy
    /// </summary>
    public string? ContentType { get; init; }
    /// <summary>
    /// "api" | "static" | "file"; affects how the chat layer handles the response
    /// </summary>
    public string EndpointKind { get; init; } = "api";
    /// <summary>
    /// Usage instruction appended to the tool description (from Mcp.Instruction)
    /// </summary>
    public string? Instruction { get; init; }
}
