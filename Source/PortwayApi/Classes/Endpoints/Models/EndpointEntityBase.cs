namespace PortwayApi.Classes;

using System.Collections.Frozen;
using System.Text.Json.Serialization;

/// <summary>
/// Configuration every endpoint type accepts, whatever its entity.json otherwise contains
/// </summary>
public abstract class EndpointEntityBase
{
    /// <summary>
    /// When false the endpoint answers 503 instead of serving
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Excludes the endpoint from the OpenAPI documentation; it keeps serving requests
    /// </summary>
    public bool Hidden { get; set; } = false;

    /// <summary>
    /// Alias kept so configs written against the earlier name still load
    /// </summary>
    [JsonInclude]
    public bool IsPrivate { set => Hidden = value; }

    /// <summary>
    /// Marks the endpoint's operations deprecated in the OpenAPI document
    /// </summary>
    public bool Deprecated { get; set; } = false;

    /// <summary>
    /// Date the endpoint was deprecated, sent as the Deprecation header
    /// </summary>
    public DateTimeOffset? DeprecatedSince { get; set; }

    /// <summary>
    /// Announced retirement date, sent as the Sunset header; the endpoint keeps serving after it until disabled
    /// </summary>
    public DateTimeOffset? Sunset { get; set; }

    /// <summary>
    /// Stable or Experimental, shown in the API reference; Deprecated takes precedence
    /// </summary>
    public string? Stability { get; set; }

    /// <summary>
    /// Labels on the endpoint's operations in the API reference
    /// </summary>
    public List<EndpointBadge>? Badges { get; set; }

    public McpSettings? Mcp { get; set; }

    /// <summary>
    /// Environments allowed to reach this endpoint; all of them when unset
    /// </summary>
    public List<string>? AllowedEnvironments { get; set; }

    /// <summary>
    /// Groups related endpoints (e.g. "CRM"); overrides the namespace inferred from the folder structure
    /// </summary>
    public string? Namespace { get; set; }

    /// <summary>
    /// Label for this endpoint in the documentation and the web UI
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Title of the namespace group in the documentation
    /// </summary>
    public string? NamespaceDisplayName { get; set; }

    /// <summary>
    /// Description of the namespace group in the documentation
    /// </summary>
    public string? NamespaceDescription { get; set; }

    public Documentation? Documentation { get; set; }

    /// <summary>
    /// Inbound tenant header to its target column, parameter or upstream header
    /// </summary>
    public Dictionary<string, string>? Tenancy { get; set; }

    /// <summary>
    /// Copies the settings shared by every endpoint type onto a parsed definition
    /// </summary>
    public void ApplyTo(EndpointDefinition definition)
    {
        definition.Enabled = Enabled;
        definition.Hidden = Hidden;
        definition.Deprecated = Deprecated;
        definition.DeprecatedSince = DeprecatedSince;
        definition.Sunset = Sunset;
        definition.Stability = Stability;
        definition.Badges = Badges;
        definition.Mcp = Mcp;
        definition.AllowedEnvironments = AllowedEnvironments;
        definition.Documentation = Documentation;
        definition.Namespace = Namespace;
        definition.DisplayName = DisplayName;
        definition.NamespaceDisplayName = NamespaceDisplayName;
        definition.NamespaceDescription = NamespaceDescription;
        definition.Tenancy = Tenancy is { Count: > 0 }
            ? new Dictionary<string, string>(Tenancy, StringComparer.OrdinalIgnoreCase).ToFrozenDictionary(StringComparer.OrdinalIgnoreCase)
            : null;
    }
}
