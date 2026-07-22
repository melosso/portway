using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace PortwayApi.Helpers;

// 201 Created (SQL POST, Webhook POST)
public sealed record CreatedResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("result")]  object? Result = null,
    [property: JsonPropertyName("id")]      object? Id = null)
{
    public static CreatedResponse Of(string message, object? result = null, object? id = null)
        => new(true, message, result, id);
}
