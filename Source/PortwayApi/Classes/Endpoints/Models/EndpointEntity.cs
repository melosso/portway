namespace PortwayApi.Classes;

/// <summary>Represents an endpoint entity with support for both proxy and SQL endpoints</summary>
public class EndpointEntity : EndpointEntityBase
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
    public string? WriteMode { get; set; }

    public string? DatabaseObjectType { get; set; } = "Table"; // Table, View, TableValuedFunction
    public List<TVFParameter>? FunctionParameters { get; set; }

    /// <summary>To-one navigations exposed via OData $expand (SQL Table/View endpoints only)</summary>
    public List<EndpointRelationship>? Relationships { get; set; }

    // Proxy endpoint properties
    public string? Url { get; set; }
    public List<string>? Methods { get; set; }
    public List<DeletePattern>? DeletePatterns { get; set; }

    public string Type { get; set; } = "Standard"; // Standard, SQL, Composite
    public CompositeDefinition? CompositeConfig { get; set; }

    public Dictionary<string, object>? CustomProperties { get; set; }
}
