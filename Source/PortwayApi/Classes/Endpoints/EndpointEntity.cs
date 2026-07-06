namespace PortwayApi.Classes;

/// <summary>Represents an endpoint entity with support for both proxy and SQL endpoints</summary>
public class EndpointEntity
{
    // SQL endpoint properties
    public string? DatabaseObjectName { get; set; }
    public string? DatabaseSchema { get; set; }
    public List<string>? AllowedColumns { get; set; }
    public List<string>? RequiredColumns { get; set; }
    public Dictionary<string, ColumnValidationRule>? ColumnValidation { get; set; }
    public string? Procedure { get; set; }
    public List<string>? AllowedMethods { get; set; }
    public string? PrimaryKey { get; set; }

    public string? DatabaseObjectType { get; set; } = "Table"; // Table, View, TableValuedFunction
    public List<TVFParameter>? FunctionParameters { get; set; }

    // Proxy endpoint properties
    public string? Url { get; set; }
    public List<string>? Methods { get; set; }
    public List<DeletePattern>? DeletePatterns { get; set; }

    // Shared properties
    public bool IsPrivate { get; set; } = false;
    public McpSettings? Mcp { get; set; }
    public string Type { get; set; } = "Standard"; // Standard, SQL, Composite
    public CompositeDefinition? CompositeConfig { get; set; }
    public List<string>? AllowedEnvironments { get; set; }

    /// <summary>Optional namespace for grouping related endpoints (e.g. "CRM"); overrides namespace inferred from folder structure</summary>
    public string? Namespace { get; set; }

    /// <summary>Display name for this specific endpoint (e.g. "Account Management"); used in OpenAPI documentation and UI displays</summary>
    public string? DisplayName { get; set; }

    /// <summary>Display name for the namespace (e.g. "Customer Relationship Management"); used as documentation tag description and grouping</summary>
    public string? NamespaceDisplayName { get; set; }

    // OpenAPI documentation properties
    public Documentation? Documentation { get; set; }

    // Custom properties for extended functionality
    public Dictionary<string, object>? CustomProperties { get; set; }
}
