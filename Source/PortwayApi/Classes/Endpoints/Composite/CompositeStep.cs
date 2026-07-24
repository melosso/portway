namespace PortwayApi.Classes;

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

/// <summary>Represents a step within a composite endpoint process</summary>
public class CompositeStep
{
    public string Name { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string Method { get; set; } = "POST";
    public string? DependsOn { get; set; }
    public bool IsArray { get; set; } = false;
    public string? ArrayProperty { get; set; }
    public string? SourceProperty { get; set; }
    public bool EmptyBody { get; set; } = false;
    public Dictionary<string, string> TemplateTransformations { get; set; } = new();
}
