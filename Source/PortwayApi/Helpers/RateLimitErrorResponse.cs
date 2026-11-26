using System.Text.Json.Serialization;

namespace PortwayApi.Helpers;

public sealed record RateLimitErrorResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("retrytime")] string RetryTime)
{
    public static RateLimitErrorResponse Of(string error, string retryTime) => new(false, error, retryTime);
}
