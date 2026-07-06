using System;
using System.Collections.Generic;

namespace PortwayApi.Services.Caching;

/// <summary>Options specific to Redis caching</summary>
public class RedisOptions
{
    /// <summary>Redis connection string</summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>Instance name for Redis (used as prefix for keys)</summary>
    public string InstanceName { get; set; } = "Portway:";

    /// <summary>Redis database ID to use</summary>
    public int Database { get; set; } = 0;

    /// <summary>Whether to use SSL for Redis connection</summary>
    public bool UseSsl { get; set; } = false;

    /// <summary>Connection timeout in milliseconds</summary>
    public int ConnectTimeoutMs { get; set; } = 5000;

    /// <summary>Whether to abort connect if connection fails</summary>
    public bool AbortOnConnectFail { get; set; } = false;

    /// <summary>Whether to fall back to memory cache if Redis is unavailable</summary>
    public bool FallbackToMemoryCache { get; set; } = true;

    /// <summary>Maximum retry attempts for Redis operations</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Retry delay in milliseconds</summary>
    public int RetryDelayMs { get; set; } = 200;
}
