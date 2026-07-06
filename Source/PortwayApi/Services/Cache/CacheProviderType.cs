using System;
using System.Collections.Generic;

namespace PortwayApi.Services.Caching;

/// <summary>Cache provider types supported by the application</summary>
public enum CacheProviderType
{
    /// <summary>In-memory cache (default)</summary>
    Memory,

    /// <summary>Redis distributed cache</summary>
    Redis
}
