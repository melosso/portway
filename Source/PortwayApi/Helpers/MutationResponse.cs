using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace PortwayApi.Helpers;

// Mutation success (PUT, PATCH, DELETE, Composite fallback)
public sealed record MutationResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("result")]  object? Result = null)
{
    public static MutationResponse Of(string message, object? result = null)
        => new(true, message, result);
}
