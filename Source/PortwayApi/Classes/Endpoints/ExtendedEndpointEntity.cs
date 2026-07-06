namespace PortwayApi.Classes;

/// <summary>Represents an endpoint entity with extended support for composite operations</summary>
public class ExtendedEndpointEntity
{
    public string Url { get; set; } = string.Empty;
    public List<string> Methods { get; set; } = new List<string>();
    public string Type { get; set; } = "Standard"; // "Standard" or "Composite"
    public CompositeDefinition? CompositeConfig { get; set; }
    public bool IsPrivate { get; set; } = false; // If true, endpoint won't be exposed in the API (documentation)
    public McpSettings? Mcp { get; set; }
    public List<string>? AllowedEnvironments { get; set; } // List of environments that can access this endpoint

    /// <summary>Optional namespace for grouping related endpoints (e.g. "CRM"); overrides namespace inferred from folder structure</summary>
    public string? Namespace { get; set; }

    /// <summary>Display name for this specific endpoint (e.g. "Weather Service"); used in OpenAPI documentation and UI displays</summary>
    public string? DisplayName { get; set; }

    /// <summary>Display name for the namespace (e.g. "External Services"); used as documentation tag description and grouping</summary>
    public string? NamespaceDisplayName { get; set; }

    /// <summary>DELETE operation patterns</summary>
    public List<DeletePattern>? DeletePatterns { get; set; }

    public Documentation? Documentation { get; set; } // OpenAPI documentation settings
    public Dictionary<string, object>? CustomProperties { get; set; } // Custom properties for extended functionality
}
