using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace PortwayApi.Helpers;

// 422 Validation detail item
public sealed record ValidationDetail(
    [property: JsonPropertyName("field")]   string Field,
    [property: JsonPropertyName("message")] string Message);
