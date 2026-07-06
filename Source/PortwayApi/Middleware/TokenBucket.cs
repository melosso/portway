namespace PortwayApi.Middleware;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Text.Json;
using Serilog;
using PortwayApi.Auth;

// Token bucket algorithm implementation
public class TokenBucket
{
    private readonly int _capacity;
    private readonly TimeSpan _refillTime;
    private readonly object _syncLock = new object();
    private readonly string _bucketId;

    private double _tokens;
    private DateTime _lastRefill;
    private int _requestCount = 0;
    
    // Logging suppression mechanism
    private DateTime _lastLoggedBlockTime = DateTime.MinValue;
    private readonly TimeSpan _logSuppressDuration = TimeSpan.FromSeconds(5);

    public TokenBucket(int capacity, TimeSpan refillTime, string bucketId)
    {
        _capacity = capacity;
        _refillTime = refillTime;
        _tokens = capacity;
        _lastRefill = DateTime.UtcNow;
        _bucketId = bucketId;
        
        Log.Debug("Token bucket created for a bucket: Capacity={Capacity}, RefillTime={RefillTime}s", 
            capacity, refillTime.TotalSeconds);
    }

    public bool TryConsume(int tokenCount, Microsoft.Extensions.Logging.ILogger? logger)
    {
        lock (_syncLock)
        {
            var requestNum = Interlocked.Increment(ref _requestCount);
            
            RefillTokens(logger);
            
            if (_tokens >= tokenCount)
            {
                _tokens -= tokenCount;
                if (logger != null)
                {
                    logger.LogDebug("Request #{RequestNum} for a bucket ALLOWED", requestNum);
                }
                return true;
            }

            // Suppress repeated logging for the same bucket
            var now = DateTime.UtcNow;
            if (logger != null && (now - _lastLoggedBlockTime) >= _logSuppressDuration)
            {
                logger.LogWarning(
                    "Request #{RequestNum} for a bucket BLOCKED - Tokens: {Tokens:F2}/{Capacity} < {TokenCount}", 
                    requestNum, _tokens, _capacity, tokenCount
                );
                
                Log.Warning(
                    "Rate limit reached for a bucket: {Tokens:F2}/{Capacity} tokens available, {TokenCount} required", 
                    _tokens, _capacity, tokenCount
                );

                _lastLoggedBlockTime = now;
            }
                    
            return false;
        }
    }

    public int GetRemainingTokens()
    {
        lock (_syncLock)
        {
            RefillTokens(null); // Ensure tokens are up-to-date before reporting
            return (int)Math.Floor(_tokens);
        }
    }

    public int GetCapacity()
    {
        return _capacity;
    }

    public DateTime GetResetTime()
    {
        lock (_syncLock)
        {
            RefillTokens(null);
            // Calculate when tokens will fully replenish
            var secondsToFull = (_capacity - _tokens) * _refillTime.TotalSeconds / _capacity;
            return DateTime.UtcNow.AddSeconds(secondsToFull);
        }
    }

    private void RefillTokens(Microsoft.Extensions.Logging.ILogger? logger)
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastRefill).TotalSeconds;
        
        if (elapsed <= 0)
            return;

        // Calculate tokens to add based on elapsed time
        var tokensToAdd = elapsed * (_capacity / _refillTime.TotalSeconds);
        
        if (tokensToAdd > 0.01 && logger != null) // Only log meaningful refills
        {
            logger.LogDebug("Refilling tokens for a bucket: +{TokensToAdd:F2} after {Elapsed:F2}s", 
                tokensToAdd, elapsed);
        }
        
        _tokens = Math.Min(_capacity, _tokens + tokensToAdd);
        _lastRefill = now;
    }
}
