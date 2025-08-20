using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Serilog;
using System.Text;
using System.Threading;
using Polly;
using Polly.Retry;
using System.Net.Sockets;

namespace PortwayApi.Services.Caching;

/// <summary>
/// Redis implementation of the cache provider
/// </summary>
public class RedisCacheProvider : ICacheProvider, IDisposable
{
    private readonly CacheOptions _options;
    private readonly RedisOptions _redisOptions;
    private readonly ConnectionMultiplexer? _redis;
    private readonly IDatabase? _db;
    private readonly string _instanceName;
    private readonly AsyncRetryPolicy _retryPolicy;
    private bool _isConnected;
    private readonly ICacheProvider _fallbackProvider;
    private readonly SemaphoreSlim _connectionLock = new SemaphoreSlim(1, 1);
    private DateTime _lastConnectionAttempt = DateTime.MinValue;
    private readonly TimeSpan _reconnectInterval = TimeSpan.FromSeconds(30);

    public RedisCacheProvider(
        IOptions<CacheOptions> options, 
        MemoryCacheProvider fallbackProvider)
    {
        _options = options.Value;
        _redisOptions = _options.Redis;
        _instanceName = _redisOptions.InstanceName;
        _fallbackProvider = fallbackProvider;

        // Create retry policy for Redis operations
        _retryPolicy = Policy
            .Handle<RedisConnectionException>()
            .Or<SocketException>()
            .Or<RedisTimeoutException>()
            .WaitAndRetryAsync(
                _redisOptions.MaxRetryAttempts,
                retryAttempt => TimeSpan.FromMilliseconds(_redisOptions.RetryDelayMs * Math.Pow(2, retryAttempt - 1)),
                (ex, timeSpan, retryCount, context) =>
                {
                    Log.Warning(ex, "Redis operation failed, retrying ({RetryCount}/{MaxRetries}) after {RetryDelay}ms", 
                        retryCount, _redisOptions.MaxRetryAttempts, timeSpan.TotalMilliseconds);
                }
            );

        try
        {
            // Configure Redis connection
            var configOptions = ConfigurationOptions.Parse(_redisOptions.ConnectionString);
            configOptions.AbortOnConnectFail = _redisOptions.AbortOnConnectFail;
            configOptions.ConnectTimeout = _redisOptions.ConnectTimeoutMs;
            configOptions.SyncTimeout = _redisOptions.ConnectTimeoutMs;
            configOptions.Ssl = _redisOptions.UseSsl;

            // Connect to Redis
            _redis = ConnectionMultiplexer.Connect(configOptions);
            _db = _redis.GetDatabase(_redisOptions.Database);
            _isConnected = _redis.IsConnected;

            // Subscribe to connection events
            _redis.ConnectionFailed += OnConnectionFailed;
            _redis.ConnectionRestored += OnConnectionRestored;
            
            Log.Information("✅ Successfully connected to Redis at {ConnectionString}, database: {Database}", 
                _redisOptions.ConnectionString, _redisOptions.Database);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ Failed to connect to Redis at {ConnectionString}", _redisOptions.ConnectionString);
            _isConnected = false;
            
            if (!_redisOptions.FallbackToMemoryCache)
            {
                throw;
            }
        }
    }

    /// <summary>
    /// Gets the cache provider type
    /// </summary>
    public string ProviderType => "Redis";

    /// <summary>
    /// Whether the Redis connection is active
    /// </summary>
    public bool IsConnected => _isConnected;

    /// <summary>
    /// Gets a value from the cache
    /// </summary>
    public async Task<T?> GetAsync<T>(string key) where T : class
    {
        string redisKey = GetFormattedKey(key);
        
        try
        {
            if (!IsConnected)
            {
                await TryReconnectAsync();
                
                // If still not connected and fallback enabled, use memory cache
                if (!IsConnected && _redisOptions.FallbackToMemoryCache)
                {
                    return await _fallbackProvider.GetAsync<T>(key);
                }
                else if (!IsConnected)
                {
                    Log.Warning("⚠️ Redis not connected, cannot get key: {Key}", redisKey);
                    return null;
                }
            }

            // Use retry policy for Redis operations
            return await _retryPolicy.ExecuteAsync(async () =>
            {
                RedisValue value = await _db!.StringGetAsync(redisKey);
                
                if (value.HasValue)
                {
                    Log.Debug("📋 Redis cache hit for key: {Key}", redisKey);
                    
                    try
                    {
                        return JsonSerializer.Deserialize<T>(value.ToString());
                    }
                    catch (JsonException ex)
                    {
                        Log.Error(ex, "❌ Failed to deserialize cached value for key: {Key}", redisKey);
                        return null;
                    }
                }
                
                Log.Debug("📋 Redis cache miss for key: {Key}", redisKey);
                return null;
            });
        }
        catch (Exception ex) when (ex is not RedisConnectionException)
        {
            Log.Error(ex, "❌ Error retrieving value from Redis for key: {Key}", redisKey);
            
            // If fallback is enabled, try memory cache
            if (_redisOptions.FallbackToMemoryCache)
            {
                Log.Information("⚠️ Falling back to memory cache for key: {Key}", key);
                return await _fallbackProvider.GetAsync<T>(key);
            }
        }
        
        return null;
    }

    /// <summary>
    /// Sets a value in the cache
    /// </summary>
    public async Task SetAsync<T>(string key, T value, TimeSpan expiration) where T : class
    {
        string redisKey = GetFormattedKey(key);
        
        try
        {
            if (!IsConnected)
            {
                await TryReconnectAsync();
                
                // If still not connected and fallback enabled, use memory cache
                if (!IsConnected && _redisOptions.FallbackToMemoryCache)
                {
                    await _fallbackProvider.SetAsync(key, value, expiration);
                    return;
                }
                else if (!IsConnected)
                {
                    Log.Warning("⚠️ Redis not connected, cannot set key: {Key}", redisKey);
                    return;
                }
            }

            // Serialize the value
            string serializedValue = JsonSerializer.Serialize(value);
            
            // Use retry policy for Redis operations
            await _retryPolicy.ExecuteAsync(async () =>
            {
                bool success = await _db!.StringSetAsync(
                    redisKey,
                    serializedValue,
                    expiration);
                    
                if (success)
                {
                    Log.Debug("💾 Added item to Redis cache: {Key}, expires in {Duration}s", 
                        redisKey, expiration.TotalSeconds);
                }
                else
                {
                    Log.Warning("⚠️ Failed to add item to Redis cache: {Key}", redisKey);
                }
            });
        }
        catch (Exception ex) when (ex is not RedisConnectionException)
        {
            Log.Error(ex, "❌ Error adding value to Redis for key: {Key}", redisKey);
            
            // If fallback is enabled, use memory cache
            if (_redisOptions.FallbackToMemoryCache)
            {
                Log.Information("⚠️ Falling back to memory cache for key: {Key}", key);
                await _fallbackProvider.SetAsync(key, value, expiration);
            }
        }
    }

    /// <summary>
    /// Removes an item from the cache
    /// </summary>
    public async Task RemoveAsync(string key)
    {
        string redisKey = GetFormattedKey(key);
        
        try
        {
            if (!IsConnected)
            {
                await TryReconnectAsync();
                
                // If still not connected and fallback enabled, use memory cache
                if (!IsConnected && _redisOptions.FallbackToMemoryCache)
                {
                    await _fallbackProvider.RemoveAsync(key);
                    return;
                }
                else if (!IsConnected)
                {
                    Log.Warning("⚠️ Redis not connected, cannot remove key: {Key}", redisKey);
                    return;
                }
            }

            // Use retry policy for Redis operations
            await _retryPolicy.ExecuteAsync(async () =>
            {
                bool success = await _db!.KeyDeleteAsync(redisKey);
                
                if (success)
                {
                    Log.Debug("🗑️ Removed item from Redis cache: {Key}", redisKey);
                }
            });
            
            // Always attempt to remove from fallback cache if enabled
            if (_redisOptions.FallbackToMemoryCache)
            {
                await _fallbackProvider.RemoveAsync(key);
            }
        }
        catch (Exception ex) when (ex is not RedisConnectionException)
        {
            Log.Error(ex, "❌ Error removing value from Redis for key: {Key}", redisKey);
            
            // If fallback is enabled, use memory cache
            if (_redisOptions.FallbackToMemoryCache)
            {
                await _fallbackProvider.RemoveAsync(key);
            }
        }
    }

    /// <summary>
    /// Checks if a cache key exists
    /// </summary>
    public async Task<bool> ExistsAsync(string key)
    {
        string redisKey = GetFormattedKey(key);
        
        try
        {
            if (!IsConnected)
            {
                await TryReconnectAsync();
                
                // If still not connected and fallback enabled, use memory cache
                if (!IsConnected && _redisOptions.FallbackToMemoryCache)
                {
                    return await _fallbackProvider.ExistsAsync(key);
                }
                else if (!IsConnected)
                {
                    Log.Warning("⚠️ Redis not connected, cannot check key: {Key}", redisKey);
                    return false;
                }
            }

            // Use retry policy for Redis operations
            return await _retryPolicy.ExecuteAsync(async () =>
            {
                return await _db!.KeyExistsAsync(redisKey);
            });
        }
        catch (Exception ex) when (ex is not RedisConnectionException)
        {
            Log.Error(ex, "❌ Error checking existence in Redis for key: {Key}", redisKey);
            
            // If fallback is enabled, use memory cache
            if (_redisOptions.FallbackToMemoryCache)
            {
                return await _fallbackProvider.ExistsAsync(key);
            }
        }
        
        return false;
    }

    /// <summary>
    /// Refreshes the expiration time for a cached item
    /// </summary>
    public async Task<bool> RefreshExpirationAsync(string key, TimeSpan expiration)
    {
        string redisKey = GetFormattedKey(key);
        
        try
        {
            if (!IsConnected)
            {
                await TryReconnectAsync();
                
                // If still not connected and fallback enabled, use memory cache
                if (!IsConnected && _redisOptions.FallbackToMemoryCache)
                {
                    return await _fallbackProvider.RefreshExpirationAsync(key, expiration);
                }
                else if (!IsConnected)
                {
                    Log.Warning("⚠️ Redis not connected, cannot refresh expiration for key: {Key}", redisKey);
                    return false;
                }
            }

            // Use retry policy for Redis operations
            return await _retryPolicy.ExecuteAsync(async () =>
            {
                // Check if key exists
                if (await _db!.KeyExistsAsync(redisKey))
                {
                    // Extend expiration
                    bool success = await _db.KeyExpireAsync(redisKey, expiration);
                    
                    if (success)
                    {
                        Log.Debug("🔄 Refreshed expiration for Redis cache item: {Key}, new duration: {Duration}s", 
                            redisKey, expiration.TotalSeconds);
                    }
                    
                    return success;
                }
                
                return false;
            });
        }
        catch (Exception ex) when (ex is not RedisConnectionException)
        {
            Log.Error(ex, "❌ Error refreshing expiration in Redis for key: {Key}", redisKey);
            
            // If fallback is enabled, use memory cache
            if (_redisOptions.FallbackToMemoryCache)
            {
                return await _fallbackProvider.RefreshExpirationAsync(key, expiration);
            }
        }
        
        return false;
    }

    /// <summary>
    /// Acquires a distributed lock for the specified key
    /// </summary>
    public async Task<IDisposable?> AcquireLockAsync(string lockKey, TimeSpan expiryTime, TimeSpan waitTime, TimeSpan retryTime)
    {
        string redisLockKey = GetFormattedKey($"lock:{lockKey}");
        string lockValue = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;
        
        try
        {
            if (!IsConnected)
            {
                await TryReconnectAsync();
                
                // If still not connected and fallback enabled, use memory cache
                if (!IsConnected && _redisOptions.FallbackToMemoryCache)
                {
                    return await _fallbackProvider.AcquireLockAsync(lockKey, expiryTime, waitTime, retryTime);
                }
                else if (!IsConnected)
                {
                    Log.Warning("⚠️ Redis not connected, cannot acquire lock: {Key}", redisLockKey);
                    return null;
                }
            }

            // Try to acquire the lock with retry until timeout
            while (DateTime.UtcNow - startTime < waitTime)
            {
                // Use retry policy for Redis operations
                bool acquired = await _retryPolicy.ExecuteAsync(async () =>
                {
                    return await _db!.StringSetAsync(
                        redisLockKey,
                        lockValue,
                        expiryTime,
                        When.NotExists);
                });
                
                if (acquired)
                {
                    Log.Debug("🔒 Acquired Redis lock for key: {LockKey}", redisLockKey);
                    return new RedisLockHandle(this, redisLockKey, lockValue, expiryTime);
                }
                
                // Wait before trying again
                await Task.Delay(retryTime);
            }
            
            Log.Warning("⏱️ Timed out waiting for Redis lock: {LockKey}", redisLockKey);
            
            // If fallback is enabled, try memory cache
            if (_redisOptions.FallbackToMemoryCache)
            {
                Log.Information("⚠️ Falling back to memory lock for key: {Key}", lockKey);
                return await _fallbackProvider.AcquireLockAsync(lockKey, expiryTime, waitTime, retryTime);
            }
            
            return null;
        }
        catch (Exception ex) when (ex is not RedisConnectionException)
        {
            Log.Error(ex, "❌ Error acquiring Redis lock for key: {Key}", redisLockKey);
            
            // If fallback is enabled, try memory cache
            if (_redisOptions.FallbackToMemoryCache)
            {
                Log.Information("⚠️ Falling back to memory lock for key: {Key}", lockKey);
                return await _fallbackProvider.AcquireLockAsync(lockKey, expiryTime, waitTime, retryTime);
            }
        }
        
        return null;
    }

    /// <summary>
    /// Formats a key with the Redis instance prefix
    /// </summary>
    private string GetFormattedKey(string key)
    {
        return $"{_instanceName}{key}";
    }

    /// <summary>
    /// Handler for Redis connection failed event
    /// </summary>
    private void OnConnectionFailed(object? sender, ConnectionFailedEventArgs args)
    {
        _isConnected = false;
        Log.Error("❌ Redis connection failed: {FailureType} - {Exception}", 
            args.FailureType, args.Exception?.Message);
    }

    /// <summary>
    /// Handler for Redis connection restored event
    /// </summary>
    private void OnConnectionRestored(object? sender, ConnectionFailedEventArgs args)
    {
        _isConnected = true;
        Log.Information("✅ Redis connection restored");
    }

    /// <summary>
    /// Try to reconnect to Redis if needed
    /// </summary>
    private async Task TryReconnectAsync()
    {
        // Avoid too frequent reconnection attempts
        if (_isConnected || (DateTime.UtcNow - _lastConnectionAttempt) < _reconnectInterval)
        {
            return;
        }

        // Lock to prevent multiple reconnection attempts
        if (!await _connectionLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            _lastConnectionAttempt = DateTime.UtcNow;
            
            if (_redis != null && !_redis.IsConnected)
            {
                Log.Information("🔄 Attempting to reconnect to Redis...");
                
                try
                {
                    await _redis.GetDatabase().PingAsync();
                    _isConnected = true;
                    Log.Information("✅ Successfully reconnected to Redis");
                }
                catch (Exception ex)
                {
                    _isConnected = false;
                    Log.Error(ex, "❌ Failed to reconnect to Redis");
                }
            }
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <summary>
    /// Releases the lock directly (used by LockHandle)
    /// </summary>
    internal async Task<bool> ReleaseLockAsync(string lockKey, string lockValue)
    {
        try
        {
            if (!IsConnected)
            {
                await TryReconnectAsync();
                if (!IsConnected)
                {
                    Log.Warning("⚠️ Redis not connected, cannot release lock: {Key}", lockKey);
                    return false;
                }
            }

            // Script to release the lock only if it belongs to us
            // This ensures we don't accidentally release someone else's lock if ours expired
            string script = @"
                if redis.call('get', KEYS[1]) == ARGV[1] then
                    return redis.call('del', KEYS[1])
                else
                    return 0
                end";

            // Execute the script
            RedisResult result = await _db!.ScriptEvaluateAsync(
                script, 
                new RedisKey[] { lockKey }, 
                new RedisValue[] { lockValue });
            
            bool success = (long)result > 0;
            
            if (success)
            {
                Log.Debug("🔓 Released Redis lock for key: {LockKey}", lockKey);
            }
            
            return success;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ Error releasing Redis lock for key: {Key}", lockKey);
            return false;
        }
    }

    /// <summary>
    /// Extends the lock expiry time (used by LockHandle)
    /// </summary>
    internal async Task<bool> ExtendLockAsync(string lockKey, string lockValue, TimeSpan expiryTime)
    {
        try
        {
            if (!IsConnected)
            {
                await TryReconnectAsync();
                if (!IsConnected)
                {
                    Log.Warning("⚠️ Redis not connected, cannot extend lock: {Key}", lockKey);
                    return false;
                }
            }

            // Script to extend the lock only if it belongs to us
            string script = @"
                if redis.call('get', KEYS[1]) == ARGV[1] then
                    return redis.call('expire', KEYS[1], ARGV[2])
                else
                    return 0
                end";

            // Execute the script
            RedisResult result = await _db!.ScriptEvaluateAsync(
                script, 
                new RedisKey[] { lockKey }, 
                new RedisValue[] { lockValue, (int)expiryTime.TotalSeconds });
            
            bool success = (long)result > 0;
            
            if (success)
            {
                Log.Debug("🔄 Extended Redis lock for key: {LockKey}", lockKey);
            }
            
            return success;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ Error extending Redis lock for key: {Key}", lockKey);
            return false;
        }
    }

    /// <summary>
    /// Disposes the Redis connection
    /// </summary>
    public void Dispose()
    {
        _connectionLock.Dispose();
        
        if (_redis != null)
        {
            try
            {
                _redis.ConnectionFailed -= OnConnectionFailed;
                _redis.ConnectionRestored -= OnConnectionRestored;
                _redis.Close();
                _redis.Dispose();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "❌ Error disposing Redis connection");
            }
        }
        
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Redis-based lock handle implementation
    /// </summary>
    private class RedisLockHandle : ILockHandle
    {
        private readonly RedisCacheProvider _provider;
        private readonly string _lockKey;
        private readonly string _lockValue;
        private bool _isDisposed;
        private bool _isReleased;

        public RedisLockHandle(
            RedisCacheProvider provider, 
            string lockKey, 
            string lockValue, 
            TimeSpan expiryTime)
        {
            _provider = provider;
            _lockKey = lockKey;
            _lockValue = lockValue;
            ExpiresAt = DateTime.UtcNow.Add(expiryTime);
        }

        public string Key => _lockKey;
        
        public DateTime ExpiresAt { get; private set; }
        
        public bool IsValid => !_isDisposed && !_isReleased && DateTime.UtcNow < ExpiresAt;

        public async Task<bool> ExtendAsync(TimeSpan expiryTime)
        {
            if (_isDisposed || _isReleased)
            {
                return false;
            }

            bool extended = await _provider.ExtendLockAsync(_lockKey, _lockValue, expiryTime);
            
            if (extended)
            {
                ExpiresAt = DateTime.UtcNow.Add(expiryTime);
            }
            
            return extended;
        }

        public async Task ReleaseAsync()
        {
            if (!_isReleased && !_isDisposed)
            {
                _isReleased = true;
                await _provider.ReleaseLockAsync(_lockKey, _lockValue);
            }
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                if (!_isReleased)
                {
                    _isReleased = true;
                    _provider.ReleaseLockAsync(_lockKey, _lockValue).GetAwaiter().GetResult();
                }
                
                _isDisposed = true;
            }
            
            GC.SuppressFinalize(this);
        }
    }
}
