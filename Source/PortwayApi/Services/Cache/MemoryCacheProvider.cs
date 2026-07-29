using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Serilog;

namespace PortwayApi.Services.Caching;

/// <summary>In-memory implementation of the cache provider</summary>
public class MemoryCacheProvider : ICacheProvider
{
    private readonly IMemoryCache _cache;
    private readonly CacheOptions _options;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new ConcurrentDictionary<string, SemaphoreSlim>();

    // Lock keys carry the full request URL, so the table is pruned of unheld entries above this size
    private const int MaxTrackedLocks = 1024;
    private readonly object _lockTableGate = new();

    public MemoryCacheProvider(IOptions<CacheOptions> options)
    {
        _options = options.Value;
        
        // Create memory cache with appropriate limits
        var memoryCacheOptions = new MemoryCacheOptions
        {
            SizeLimit = _options.MemoryCacheMaxItems
        };
        
        _cache = new MemoryCache(memoryCacheOptions);
    }

    /// <summary>Number of live lock entries, exposed for diagnostics and tests</summary>
    internal int TrackedLockCount => _locks.Count;

    /// <summary>Gets the cache provider type</summary>
    public string ProviderType => "Memory";

    /// <summary>Memory cache is always connected</summary>
    public bool IsConnected => true;

    /// <summary>Gets a value from the cache</summary>
    public Task<T?> GetAsync<T>(string key) where T : class
    {
        if (_cache.TryGetValue(key, out T? result))
        {
            Log.Debug("Cache hit for key: {Key}", key);
            return Task.FromResult(result);
        }

        Log.Debug("Cache miss for key: {Key}", key);
        return Task.FromResult<T?>(null);
    }

    /// <summary>Sets a value in the cache</summary>
    public Task SetAsync<T>(string key, T value, TimeSpan expiration) where T : class
    {
        // Set cache options with appropriate size
        var entryOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiration,
            Size = 1 // Default size, can be made more accurate if needed
        };

        _cache.Set(key, value, entryOptions);
        Log.Debug("Added item to memory cache: {Key}, expires in {Duration}s", key, expiration.TotalSeconds);

        return Task.CompletedTask;
    }

    /// <summary>Removes an item from the cache</summary>
    public Task RemoveAsync(string key)
    {
        _cache.Remove(key);
        Log.Debug("Removed item from memory cache: {Key}", key);
        
        return Task.CompletedTask;
    }

    /// <summary>Checks if a cache key exists</summary>
    public Task<bool> ExistsAsync(string key)
    {
        return Task.FromResult(_cache.TryGetValue(key, out _));
    }

    /// <summary>Refreshes the expiration time for a cached item</summary>
    public Task<bool> RefreshExpirationAsync(string key, TimeSpan expiration)
    {
        // Memory cache doesn't directly support changing expiration; We'd need to get the item and re-set it with new expiration
        if (_cache.TryGetValue(key, out object? value))
        {
            var entryOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiration,
                Size = 1
            };

            _cache.Set(key, value, entryOptions);
            Log.Debug("Refreshed expiration for memory cache item: {Key}, new duration: {Duration}s", 
                key, expiration.TotalSeconds);
            
            return Task.FromResult(true);
        }
        
        return Task.FromResult(false);
    }

    /// <summary>Acquires a distributed lock for the specified key</summary>
    public async Task<IDisposable?> AcquireLockAsync(string lockKey, TimeSpan expiryTime, TimeSpan waitTime, TimeSpan retryTime, CancellationToken cancellationToken = default)
    {
        string actualLockKey = $"lock:{lockKey}";

        var lockObj = GetOrCreateLock(actualLockKey);

        // Try to acquire the lock
        var waitTask = lockObj.WaitAsync(waitTime, cancellationToken);

        try
        {
            if (await waitTask.ConfigureAwait(false))
            {
                Log.Debug("Acquired memory lock for key: {LockKey}", actualLockKey);
                return new MemoryLockHandle(lockObj, actualLockKey, expiryTime);
            }

            Log.Warning("⏱️ Timed out waiting for memory lock: {LockKey}", actualLockKey);
            return null;
        }
        catch (OperationCanceledException)
        {
            Log.Warning("⏱️ Lock acquisition was canceled for key: {LockKey}", actualLockKey);
            return null;
        }
    }

    private SemaphoreSlim GetOrCreateLock(string actualLockKey)
    {
        // Gate is held for dictionary work only, so pruning cannot interleave with a handout
        lock (_lockTableGate)
        {
            if (_locks.Count >= MaxTrackedLocks)
                PruneUnheldLocks();

            return _locks.GetOrAdd(actualLockKey, _ => new SemaphoreSlim(1, 1));
        }
    }

    // ponytail: drops unheld entries only; a key pruned between handout and WaitAsync can be
    // acquired twice, costing one duplicate upstream fetch. Reference-count if that ever matters
    private void PruneUnheldLocks()
    {
        foreach (var pair in _locks)
        {
            if (pair.Value.CurrentCount == 1)
                _locks.TryRemove(pair);
        }

        Log.Debug("Pruned memory lock table down to {Count} entries", _locks.Count);
    }

    /// <summary>Memory-based lock handle implementation</summary>
    private class MemoryLockHandle : ILockHandle
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly string _lockKey;
        private bool _isDisposed;
        private bool _isReleased;

        public MemoryLockHandle(SemaphoreSlim semaphore, string lockKey, TimeSpan expiryTime)
        {
            _semaphore = semaphore;
            _lockKey = lockKey;
            ExpiresAt = DateTime.UtcNow.Add(expiryTime);
        }

        private void Release()
        {
            try
            {
                _semaphore.Release();
                Log.Debug("Released memory lock for key: {LockKey}", _lockKey);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error releasing memory lock for key: {LockKey}", _lockKey);
            }
        }

        public string Key => _lockKey;
        
        public DateTime ExpiresAt { get; private set; }
        
        public bool IsValid => !_isDisposed && !_isReleased && DateTime.UtcNow < ExpiresAt;

        public Task<bool> ExtendAsync(TimeSpan expiryTime)
        {
            if (_isDisposed || _isReleased)
            {
                return Task.FromResult(false);
            }

            ExpiresAt = DateTime.UtcNow.Add(expiryTime);
            return Task.FromResult(true);
        }

        public Task ReleaseAsync()
        {
            if (!_isReleased && !_isDisposed)
            {
                _isReleased = true;
                Release();
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                if (!_isReleased)
                {
                    Release();
                    _isReleased = true;
                }
                
                _isDisposed = true;
            }
            
            GC.SuppressFinalize(this);
        }
    }
}
