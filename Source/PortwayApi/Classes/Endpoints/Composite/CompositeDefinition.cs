namespace PortwayApi.Classes;

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

/// <summary>Defines a composite endpoint that represents a multi-step API process</summary>
public class CompositeDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<CompositeStep> Steps { get; set; } = new List<CompositeStep>();
}
