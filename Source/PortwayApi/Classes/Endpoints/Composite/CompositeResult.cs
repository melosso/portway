namespace PortwayApi.Classes;

using System.Text.Json.Serialization;

/// <summary>
/// Result of a successful composite endpoint execution; failures return an error response
/// </summary>
public sealed class CompositeResult
{
    [JsonPropertyName("success")]
    public bool Success => true;

    /// <summary>
    /// Dictionary of results from each step in the composite operation
    /// </summary>
    [JsonPropertyName("stepResults")]
    public Dictionary<string, object> StepResults { get; set; } = new();
}
