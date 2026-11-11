namespace PortwayApi.Classes;

/// <summary>
/// Represents an endpoint entity with support for both proxy and SQL endpoints
/// </summary>
public sealed class EndpointEntity : EndpointEntityBase
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

    /// <summary>
    /// To-one navigations exposed via OData $expand (SQL Table/View endpoints only)
    /// </summary>
    public List<EndpointRelationship>? Relationships { get; set; }

    // Proxy endpoint properties
    public string? Url { get; set; }
    public List<string>? Methods { get; set; }
    public List<DeletePattern>? DeletePatterns { get; set; }
    public List<string>? FallbackUrls { get; set; }

    /// <summary>
    /// Headers sent upstream. Values replace environment headers of the same name and accept <c>${secret:NAME}</c>
    /// </summary>
    public Dictionary<string, string>? Headers { get; set; }

    /// <summary>
    /// Sends the client Authorization header upstream when it also authenticated the request
    /// </summary>
    public bool ForwardAuthorization { get; set; } = false;
    public ProxyRetryOptions? Retry { get; set; }
    public ProxyResponseTransforms? ResponseTransforms { get; set; }

    /// <summary>
    /// Set when the proxied service understands OData query parameters, so the documentation can advertise them
    /// </summary>
    public bool SupportsOData { get; set; } = false;

    public string Type { get; set; } = "Standard"; // Standard, SQL, Composite
    public CompositeDefinition? CompositeConfig { get; set; }

    public Dictionary<string, object>? CustomProperties { get; set; }

    public Dictionary<string, object>? Properties { get; set; }
}
