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
        /// Gets a cached value, or default when missing
        /// </summary>
        Task<T?> GetAsync<T>(string key) where T : class;

        /// <summary>
        /// Sets a cached value with an expiration
        /// </summary>
        Task SetAsync<T>(string key, T value, TimeSpan expiration) where T : class;

        /// <summary>
        /// Removes a cached value
        /// </summary>
        Task RemoveAsync(string key);

        /// <summary>
        /// Acquires a distributed lock; dispose the handle to release it
        /// </summary>
        Task<IDisposable?> AcquireLockAsync(string lockKey, TimeSpan expiryTime, TimeSpan waitTime, TimeSpan retryTime, CancellationToken cancellationToken = default);

        /// <summary>
        /// Whether a cache key exists
        /// </summary>
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
        /// Refreshes an expiration; false when the key is missing
        /// </summary>
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
        /// Extends the lock expiration
        /// </summary>
        Task<bool> ExtendAsync(TimeSpan expiryTime);

        /// <summary>
        /// Releases the lock, also done on dispose
        /// </summary>
        Task ReleaseAsync();
    }
}