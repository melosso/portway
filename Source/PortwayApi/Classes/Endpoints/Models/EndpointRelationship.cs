namespace PortwayApi.Classes;

/// <summary>Declares a to-one navigation from a SQL endpoint to another registered SQL endpoint,
/// exposed to readers through OData $expand. Target-by-name: schema/table/columns/gates derive from the target</summary>
public class EndpointRelationship
{
    /// <summary>Navigation name used in $expand and as the nested response key (e.g. "Category")</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Name of the registered SQL endpoint this relationship points at (may be namespaced, e.g. "Product/Assortments")</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>Foreign key column on this endpoint's object (the dependent side, database column name)</summary>
    public string LocalColumn { get; set; } = string.Empty;

    /// <summary>Principal column on the target object matched by the FK (usually the target primary key)</summary>
    public string TargetColumn { get; set; } = string.Empty;

    /// <summary>Cardinality; only ToOne is supported in v1 (fork JoinClauseBuilder is to-one only)</summary>
    public string? Multiplicity { get; set; } = "ToOne";
}
