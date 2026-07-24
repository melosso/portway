namespace PortwayApi.Classes;

/// <summary>Represents a Static endpoint entity for serving predefined content</summary>
public class StaticEndpointEntity
{
    /// <summary>MIME type for the response (application/json, text/plain, image/png, etc.)</summary>
    public string ContentType { get; set; } = "text/plain";

    /// <summary>Filename containing the static content (relative to the endpoint directory)</summary>
    public string ContentFile { get; set; } = "content.txt";

    /// <summary>Whether OData filtering ($filter, $select, etc.) is enabled for this endpoint</summary>
    public bool EnableFiltering { get; set; } = false;

    /// <summary>Whether this endpoint is private (not accessible via API)</summary>
    public bool IsPrivate { get; set; } = false;

    public McpSettings? Mcp { get; set; }

    /// <summary>List of environments allowed to access this endpoint</summary>
    public List<string>? AllowedEnvironments { get; set; }

    /// <summary>Optional namespace for grouping related static endpoints (e.g. "Reports"); overrides namespace inferred from folder structure</summary>
    public string? Namespace { get; set; }

    /// <summary>Display name for this static endpoint (e.g. "Sales Report Template"); used in OpenAPI documentation and UI displays</summary>
    public string? DisplayName { get; set; }

    /// <summary>Display name for the namespace (e.g. "Reporting System"); used as documentation tag description and grouping</summary>
    public string? NamespaceDisplayName { get; set; }

    /// <summary>OpenAPI documentation for this endpoint</summary>
    public Documentation? Documentation { get; set; }
}
