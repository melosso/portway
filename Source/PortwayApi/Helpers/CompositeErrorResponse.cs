using System.Text.Json.Serialization;

namespace PortwayApi.Helpers;

public sealed record CompositeErrorResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("details")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Details,
    [property: JsonPropertyName("traceId")] string TraceId,
    [property: JsonPropertyName("step")] string Step,
    [property: JsonPropertyName("statusCode")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? StatusCode,
    [property: JsonPropertyName("completedSteps")] IReadOnlyList<string> CompletedSteps)
{
    public static CompositeErrorResponse Of(string error, string traceId, string step, IReadOnlyList<string> completedSteps,
        string? details = null, int? statusCode = null)
        => new(false, error, details, traceId, step, statusCode, completedSteps);
}
