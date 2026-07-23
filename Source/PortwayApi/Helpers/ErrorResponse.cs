using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace PortwayApi.Helpers;

// All non-validation errors
public sealed record ErrorResponse(
    [property: JsonPropertyName("success")] bool   Success,
    [property: JsonPropertyName("error")]   string Error)
{
    public static ErrorResponse Of(string error) => new(false, error);
}
