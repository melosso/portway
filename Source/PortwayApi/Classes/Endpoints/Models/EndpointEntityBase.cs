namespace PortwayApi.Classes;

using System.Text.Json.Serialization;

/// <summary>Configuration every endpoint type accepts, whatever its entity.json otherwise contains</summary>
public abstract class EndpointEntityBase
{
    /// <summary>When false the endpoint answers 503 instead of serving</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Excludes the endpoint from the OpenAPI documentation; it keeps serving requests</summary>
    public bool Hidden { get; set; } = false;

    /// <summary>Alias kept so configs written against the earlier name still load</summary>
    [JsonInclude]
    public bool IsPrivate { set => Hidden = value; }

    /// <summary>Marks the endpoint's operations deprecated in the OpenAPI document</summary>
    public bool Deprecated { get; set; } = false;

    public McpSettings? Mcp { get; set; }

    /// <summary>Environments allowed to reach this endpoint; all of them when unset</summary>
    public List<string>? AllowedEnvironments { get; set; }

    /// <summary>Groups related endpoints (e.g. "CRM"); overrides the namespace inferred from the folder structure</summary>
    public string? Namespace { get; set; }

    /// <summary>Label for this endpoint in the documentation and the web UI</summary>
    public string? DisplayName { get; set; }

    /// <summary>Label for the namespace, used as the documentation tag</summary>
    public string? NamespaceDisplayName { get; set; }

    public Documentation? Documentation { get; set; }
}
