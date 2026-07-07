using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace PortwayApi.Classes.Helpers;

// Collection responses (SQL GET, Static filtered, File list)
public sealed record CollectionResponse<T>(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("count")]   int Count,
    [property: JsonPropertyName("value")]   IReadOnlyList<T> Value,
    [property: JsonPropertyName("nextLink")] string? NextLink = null,
    [property: JsonPropertyName("totalCount")] [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? TotalCount = null)
{
    public static CollectionResponse<T> Of(IReadOnlyList<T> items, string? nextLink = null, long? totalCount = null)
        => new(true, items.Count, items, nextLink, totalCount);
}
