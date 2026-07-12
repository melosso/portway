namespace PortwayApi.Classes;

/// <summary>Represents a File endpoint entity for local file handling</summary>
public class FileEndpointEntity
{
    /// <summary>Type of storage (Local, S3, etc.)</summary>
    public string StorageType { get; set; } = "Local";

    /// <summary>Base directory for this endpoint (relative to the root storage directory)</summary>
    public string? BaseDirectory { get; set; }

    /// <summary>List of allowed file extensions</summary>
    public List<string>? AllowedExtensions { get; set; }

    /// <summary>Whether this endpoint is private (not accessible via API)</summary>
    public bool IsPrivate { get; set; } = false;

    public McpSettings? Mcp { get; set; }

    /// <summary>List of environments allowed to access this endpoint</summary>
    public List<string>? AllowedEnvironments { get; set; }

    /// <summary>Optional namespace for grouping related file endpoints (e.g. "CRM"); overrides namespace inferred from folder structure</summary>
    public string? Namespace { get; set; }

    /// <summary>Display name for this file endpoint (e.g. "Customer Documents"); used in OpenAPI documentation and UI displays</summary>
    public string? DisplayName { get; set; }

    /// <summary>Display name for the namespace (e.g. "Customer Relationship Management"); used as documentation tag description and grouping</summary>
    public string? NamespaceDisplayName { get; set; }

    /// <summary>OpenAPI documentation for this endpoint</summary>
    public Documentation? Documentation { get; set; }
}
