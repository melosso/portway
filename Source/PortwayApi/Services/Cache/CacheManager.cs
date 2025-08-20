using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Serilog;

namespace PortwayApi.Services.Caching;

/// <summary>
/// Manages caching operations using the configured provider
/// </summary>
public class CacheManager : ICacheProvider
{
    private readonly ICacheProvider _provider;
    private readonly CacheOptions _options;

    public CacheManager(
        IOptions<CacheOptions> options,
        MemoryCacheProvider memoryCacheProvider,
        RedisCacheProvider? redisCacheProvider = null)
    {
        _options = options.Value;

        // Select the appropriate provider based on configuration
        _provider = _options.ProviderType switch
        {
            CacheProviderType.Redis when redisCacheProvider != null && redisCacheProvider.IsConnected => redisCacheProvider,
            CacheProviderType.Redis when redisCacheProvider != null && _options.Redis.FallbackToMemoryCache => memoryCacheProvider,
            _ => memoryCacheProvider
        };

        Log.Debug("🔧 Cache Manager initialized with {ProviderType} provider", _provider.ProviderType);
    }

    /// <summary>
    /// Gets the cache provider type
    /// </summary>
    public string ProviderType => _provider.ProviderType;

    /// <summary>
    /// Whether the cache provider is connected
    /// </summary>
    public bool IsConnected => _provider.IsConnected;

    /// <summary>
    /// Gets a value from the cache
    /// </summary>
    public Task<T?> GetAsync<T>(string key) where T : class
    {
        if (!_options.Enabled)
        {
            return Task.FromResult<T?>(null);
        }

        return _provider.GetAsync<T>(key);
    }

    /// <summary>
    /// Sets a value in the cache
    /// </summary>
    public Task SetAsync<T>(string key, T value, TimeSpan expiration) where T : class
    {
        if (!_options.Enabled)
        {
            return Task.CompletedTask;
        }

        return _provider.SetAsync(key, value, expiration);
    }

    /// <summary>
    /// Sets a value in the cache with default expiration for the endpoint
    /// </summary>
    public Task SetAsync<T>(string key, T value, string endpointName) where T : class
    {
        if (!_options.Enabled)
        {
            return Task.CompletedTask;
        }

        TimeSpan expiration = GetCacheDurationForEndpoint(endpointName);
        return _provider.SetAsync(key, value, expiration);
    }

    /// <summary>
    /// Removes an item from the cache
    /// </summary>
    public Task RemoveAsync(string key)
    {
        if (!_options.Enabled)
        {
            return Task.CompletedTask;
        }

        return _provider.RemoveAsync(key);
    }

    /// <summary>
    /// Checks if a cache key exists
    /// </summary>
    public Task<bool> ExistsAsync(string key)
    {
        if (!_options.Enabled)
        {
            return Task.FromResult(false);
        }

        return _provider.ExistsAsync(key);
    }

    /// <summary>
    /// Refreshes the expiration time for a cached item
    /// </summary>
    public Task<bool> RefreshExpirationAsync(string key, TimeSpan expiration)
    {
        if (!_options.Enabled)
        {
            return Task.FromResult(false);
        }

        return _provider.RefreshExpirationAsync(key, expiration);
    }

    /// <summary>
    /// Refreshes the expiration time for a cached item using the default for an endpoint
    /// </summary>
    public Task<bool> RefreshExpirationAsync(string key, string endpointName)
    {
        if (!_options.Enabled)
        {
            return Task.FromResult(false);
        }

        TimeSpan expiration = GetCacheDurationForEndpoint(endpointName);
        return _provider.RefreshExpirationAsync(key, expiration);
    }

    /// <summary>
    /// Acquires a distributed lock for the specified key
    /// </summary>
    public Task<IDisposable?> AcquireLockAsync(string lockKey, TimeSpan expiryTime, TimeSpan waitTime, TimeSpan retryTime)
    {
        if (!_options.Enabled)
        {
            return Task.FromResult<IDisposable?>(null);
        }

        return _provider.AcquireLockAsync(lockKey, expiryTime, waitTime, retryTime);
    }

    /// <summary>
    /// Acquires a distributed lock with default timeout values
    /// </summary>
    public Task<IDisposable?> AcquireLockAsync(string lockKey)
    {
        if (!_options.Enabled)
        {
            return Task.FromResult<IDisposable?>(null);
        }

        return _provider.AcquireLockAsync(
            lockKey,
            TimeSpan.FromSeconds(30), // Default lock expiry
            TimeSpan.FromSeconds(10), // Default wait time
            TimeSpan.FromMilliseconds(200)); // Default retry interval
    }

    /// <summary>
    /// Determines if a response should be cached based on content type
    /// </summary>
    public bool ShouldCacheResponse(string? contentType)
    {
        if (!_options.Enabled || string.IsNullOrEmpty(contentType))
        {
            return false;
        }

        // Check against list of cacheable content types
        foreach (var allowedType in _options.CacheableContentTypes)
        {
            if (contentType.Contains(allowedType, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Get cache duration for an endpoint
    /// </summary>
    public TimeSpan GetCacheDurationForEndpoint(string endpointName)
    {
        return _options.GetCacheDurationForEndpoint(endpointName);
    }
}
