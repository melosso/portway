namespace PortwayApi.Classes;

/// <summary>
/// To-one $expand navigation to another SQL endpoint, resolved from the target
/// </summary>
public class EndpointRelationship
{
    /// <summary>
    /// Navigation name used in $expand and as the nested response key (e.g. "Category")
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Name of the registered SQL endpoint this relationship points at (may be namespaced, e.g. "Product/Assortments")
    /// </summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>
    /// Foreign key column on this endpoint's object (the dependent side, database column name)
    /// </summary>
    public string LocalColumn { get; set; } = string.Empty;

    /// <summary>
    /// Principal column on the target object matched by the FK (usually the target primary key)
    /// </summary>
    public string TargetColumn { get; set; } = string.Empty;

    /// <summary>
    /// Cardinality; only ToOne is supported in v1 (fork JoinClauseBuilder is to-one only)
    /// </summary>
    public string? Multiplicity { get; set; } = "ToOne";
}
