using System.Text.Json.Serialization;

namespace PortwayApi.Helpers;

// All non-validation errors
public sealed record ErrorResponse(
    // Whether the request was successful
    [property: JsonPropertyName("success")] bool   Success,
    // The error message to return to the client
    [property: JsonPropertyName("error")]   string Error,
    // Correlates a masked 500 with the server log; omitted from every other status
    [property: JsonPropertyName("traceId")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TraceId = null)
{
    public static ErrorResponse Of(string error) => new(false, error);

    public static ErrorResponse Traced(string error, string? traceId) => new(false, error, traceId);
}
