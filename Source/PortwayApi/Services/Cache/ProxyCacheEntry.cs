using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PortwayApi.Services.Caching;

/// <summary>
/// Cache entry for proxy responses
/// </summary>
public class ProxyCacheEntry
{
    /// <summary>
    /// Response content
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Response headers
    /// </summary>
    public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// Response status code
    /// </summary>
    public int StatusCode { get; set; } = 200;

    /// <summary>
    /// When the cache entry was created
    /// </summary>
    [JsonIgnore]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Creates a cache entry from response details
    /// </summary>
    public static ProxyCacheEntry Create(string content, Dictionary<string, string> headers, int statusCode)
    {
        return new ProxyCacheEntry
        {
            Content = content,
            Headers = headers,
            StatusCode = statusCode,
            CreatedAt = DateTime.UtcNow
        };
    }
}
