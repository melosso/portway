using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace PortwayApi.Classes.Helpers;

// 422 Validation error
public sealed record ValidationErrorResponse(
    [property: JsonPropertyName("success")] bool   Success,
    [property: JsonPropertyName("error")]   string Error,
    [property: JsonPropertyName("details")] IReadOnlyList<ValidationDetail> Details)
{
    public static ValidationErrorResponse Of(
        IEnumerable<ValidationDetail> details, string error = "Validation failed")
        => new(false, error, [.. details]);
}
