using System;
using System.Collections.Generic;

namespace PortwayApi.Services.Caching;

/// <summary>Configuration options for caching</summary>
public class CacheOptions
{
    /// <summary>Whether caching is enabled for the application</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Default cache duration in seconds</summary>
    public int DefaultCacheDurationSeconds { get; set; } = 300;

    /// <summary>Maximum number of items in memory cache</summary>
    public int MemoryCacheMaxItems { get; set; } = 10000;

    /// <summary>Size limit for memory cache in MB</summary>
    public int MemoryCacheSizeLimitMB { get; set; } = 100;

    /// <summary>Which cache provider to use</summary>
    public CacheProviderType ProviderType { get; set; } = CacheProviderType.Memory;

    /// <summary>Redis configuration options (if Redis provider is used)</summary>
    public RedisOptions Redis { get; set; } = new RedisOptions();

    /// <summary>Endpoint-specific cache durations</summary>
    public Dictionary<string, int> EndpointCacheDurations { get; set; } = new Dictionary<string, int>();

    /// <summary>Content types that should be cached</summary>
    public List<string> CacheableContentTypes { get; set; } = new List<string>
    {
        "application/json",
        "text/json",
        "application/xml",
        "text/xml"
    };

    /// <summary>Get cache duration for an endpoint</summary>
    public TimeSpan GetCacheDurationForEndpoint(string endpointName)
    {
        if (EndpointCacheDurations.TryGetValue(endpointName, out int seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }
        return TimeSpan.FromSeconds(DefaultCacheDurationSeconds);
    }
}
