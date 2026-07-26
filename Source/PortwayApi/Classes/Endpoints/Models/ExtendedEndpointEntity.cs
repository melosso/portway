namespace PortwayApi.Classes;

/// <summary>Represents an endpoint entity with extended support for composite operations</summary>
public class ExtendedEndpointEntity : EndpointEntityBase
{
    public string Url { get; set; } = string.Empty;
    public List<string> Methods { get; set; } = new List<string>();
    public string Type { get; set; } = "Standard"; // "Standard" or "Composite"
    public CompositeDefinition? CompositeConfig { get; set; }
    public List<DeletePattern>? DeletePatterns { get; set; }
    public Dictionary<string, object>? CustomProperties { get; set; }
}
