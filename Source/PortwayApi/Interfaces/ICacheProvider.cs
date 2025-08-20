using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PortwayApi.Services.Caching
{
    /// <summary>
    /// Interface for cache providers used by the application
    /// </summary>
    public interface ICacheProvider
    {
        /// <summary>
        /// Gets a value from the cache
        /// </summary>
        /// <typeparam name="T">Type of the cached item</typeparam>
        /// <param name="key">Cache key</param>
        /// <returns>The cached value or default if not found</returns>
        Task<T?> GetAsync<T>(string key) where T : class;

        /// <summary>
        /// Sets a value in the cache
        /// </summary>
        /// <typeparam name="T">Type of the item to cache</typeparam>
        /// <param name="key">Cache key</param>
        /// <param name="value">Value to cache</param>
        /// <param name="expiration">Cache expiration timespan</param>
        /// <returns>Task representing the asynchronous operation</returns>
        Task SetAsync<T>(string key, T value, TimeSpan expiration) where T : class;

        /// <summary>
        /// Removes an item from the cache
        /// </summary>
        /// <param name="key">Cache key</param>
        /// <returns>Task representing the asynchronous operation</returns>
        Task RemoveAsync(string key);

        /// <summary>
        /// Acquires a distributed lock for the specified key
        /// </summary>
        /// <param name="lockKey">Key to lock</param>
        /// <param name="expiryTime">Lock expiry time</param>
        /// <param name="waitTime">How long to wait for the lock</param>
        /// <param name="retryTime">Time between retry attempts</param>
        /// <returns>Lock handle that should be disposed to release the lock</returns>
        Task<IDisposable?> AcquireLockAsync(string lockKey, TimeSpan expiryTime, TimeSpan waitTime, TimeSpan retryTime);

        /// <summary>
        /// Checks if a cache key exists
        /// </summary>
        /// <param name="key">Cache key</param>
        /// <returns>True if the key exists in the cache</returns>
        Task<bool> ExistsAsync(string key);

        /// <summary>
        /// Gets the cache provider type
        /// </summary>
        string ProviderType { get; }

        /// <summary>
        /// Gets connection status for the cache provider
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Refreshes the expiration time for a cached item
        /// </summary>
        /// <param name="key">Cache key</param>
        /// <param name="expiration">New expiration timespan</param>
        /// <returns>True if the key was found and expiration updated</returns>
        Task<bool> RefreshExpirationAsync(string key, TimeSpan expiration);
    }

    /// <summary>
    /// Represents a distributed lock handle that can be disposed to release the lock
    /// </summary>
    public interface ILockHandle : IDisposable
    {
        /// <summary>
        /// The key being locked
        /// </summary>
        string Key { get; }
        
        /// <summary>
        /// When the lock expires
        /// </summary>
        DateTime ExpiresAt { get; }
        
        /// <summary>
        /// Whether the lock is still valid
        /// </summary>
        bool IsValid { get; }
        
        /// <summary>
        /// Extends the lock's expiration time
        /// </summary>
        /// <param name="expiryTime">New lock expiry time</param>
        /// <returns>True if the lock was extended successfully</returns>
        Task<bool> ExtendAsync(TimeSpan expiryTime);
        
        /// <summary>
        /// Releases the lock explicitly (also happens on Dispose)
        /// </summary>
        /// <returns>Task representing the asynchronous operation</returns>
        Task ReleaseAsync();
    }
}