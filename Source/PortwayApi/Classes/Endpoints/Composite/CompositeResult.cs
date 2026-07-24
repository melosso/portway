namespace PortwayApi.Classes;

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

/// <summary>Result of a composite endpoint execution with enhanced error details</summary>
public class CompositeResult
{
    /// <summary>Indicates whether the composite operation was successful</summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; } = true;

    /// <summary>Dictionary of results from each step in the composite operation</summary>
    [JsonPropertyName("stepResults")]
    public Dictionary<string, object> StepResults { get; set; } = new();

    /// <summary>The name of the step that failed (null if successful)</summary>
    [JsonPropertyName("errorStep")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorStep { get; set; }

    /// <summary>Error message if the composite operation failed</summary>
    [JsonPropertyName("errorMessage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorMessage { get; set; }

    /// <summary>Detailed error information from the underlying API</summary>
    [JsonPropertyName("errorDetail")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorDetail { get; set; }

    /// <summary>The HTTP status code of the failed request</summary>
    [JsonPropertyName("statusCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? StatusCode { get; set; }
}
