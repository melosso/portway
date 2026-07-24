namespace PortwayApi.Classes;

/// <summary>Declarative response shaping rules for proxy endpoints</summary>
public sealed class ProxyResponseTransforms
{
    public List<string>? Remove { get; set; }
    public Dictionary<string, string>? Rename { get; set; }
    public List<string>? Mask { get; set; }

    public bool HasRules =>
        Remove is { Count: > 0 } || Rename is { Count: > 0 } || Mask is { Count: > 0 };
}
