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

public class RateLimitSettings
{
    public bool Enabled { get; set; } = true;
    public int IpLimit { get; set; } = 100;
    public int IpWindow { get; set; } = 60; // seconds
    public int TokenLimit { get; set; } = 1000;
    public int TokenWindow { get; set; } = 60; // seconds
}

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
        
        Log.Debug("🪣 Token bucket created for {BucketId}: Capacity={Capacity}, RefillTime={RefillTime}s", 
            bucketId, capacity, refillTime.TotalSeconds);
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
                    logger.LogDebug("Request #{RequestNum} for {BucketId} ALLOWED", requestNum, _bucketId);
                }
                return true;
            }

            // Suppress repeated logging for the same bucket
            var now = DateTime.UtcNow;
            if (logger != null && (now - _lastLoggedBlockTime) >= _logSuppressDuration)
            {
                logger.LogWarning(
                    "Request #{RequestNum} for {BucketId} BLOCKED - Tokens: {Tokens:F2}/{Capacity} < {TokenCount}", 
                    requestNum, _bucketId, _tokens, _capacity, tokenCount
                );
                
                Log.Warning(
                    "Rate limit reached for {BucketId}: {Tokens:F2}/{Capacity} tokens available, {TokenCount} required", 
                    _bucketId, _tokens, _capacity, tokenCount
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
            logger.LogDebug("🔄 Refilling tokens for {BucketId}: +{TokensToAdd:F2} after {Elapsed:F2}s", 
                _bucketId, tokensToAdd, elapsed);
        }
        
        _tokens = Math.Min(_capacity, _tokens + tokensToAdd);
        _lastRefill = now;
    }
}

public class RateLimiter
{
    // Represents block information for a token or IP
    private class BlockInfo
    {
        public DateTime BlockedUntil { get; set; }
        public int ConsecutiveBlocks { get; set; }
        public int BlockDuration { get; set; }
    }

    private readonly RequestDelegate _next;
    private readonly RateLimitSettings _settings;
    private readonly Microsoft.Extensions.Logging.ILogger<RateLimiter> _logger;
    private readonly ConcurrentDictionary<string, TokenBucket> _buckets = new();
    private readonly ConcurrentDictionary<string, BlockInfo> _blockedTokens = new();
    private readonly string _instanceId = Guid.NewGuid().ToString()[..8];

    // Track blocked IPs with last logged time to prevent log flooding
    private readonly ConcurrentDictionary<string, (DateTime BlockedUntil, DateTime LastLogged)> _blockedIps = new();
    
    // Cache for token ID lookups to avoid repeated database calls
    private readonly ConcurrentDictionary<string, string> _tokenDisplayCache = new();
    
    // Configuration for block tracking
    private readonly int _maxConsecutiveBlocks = 3;
    private readonly TimeSpan _logCooldown = TimeSpan.FromSeconds(10);
    // Logging suppression duration for blocked IPs
    private readonly TimeSpan _logSuppressDuration = TimeSpan.FromSeconds(5);

    public RateLimiter(
        RequestDelegate next,
        IConfiguration configuration,
        Microsoft.Extensions.Logging.ILogger<RateLimiter> logger)
    {
        _next = next;
        _logger = logger;
        
        _settings = new RateLimitSettings();
        configuration.GetSection("RateLimiting").Bind(_settings);
        
        if (_settings.Enabled)
        {
            _logger.LogInformation("🚦 Rate limiter {InstanceId} initialized - Enabled: {Enabled}, IP: {IpLimit}/{IpWindow}s, Token: {TokenLimit}/{TokenWindow}s",
            _instanceId, _settings.Enabled, _settings.IpLimit, _settings.IpWindow, _settings.TokenLimit, _settings.TokenWindow);
        }
    }

    // Improved token masking method
    private string MaskToken(string token)
    {
        if (string.IsNullOrEmpty(token) || token.Length <= 8)
            return "token";

        return $"{token.Substring(0, 4)}...{token.Substring(token.Length - 4)}";
    }

    public async Task InvokeAsync(HttpContext context, AuthDbContext dbContext, TokenService tokenService)
    {
        var path = context.Request.Path.ToString().ToLower();

        // Skip rate limiting and auth for exempted paths
        if (path.StartsWith("/swagger") || path.StartsWith("/docs") || path == "/index.html")
        {
            await _next(context);
            return;
        }
        
        // Skip all checks if rate limiting is disabled
        if (!_settings.Enabled)
        {
            await _next(context);
            return;
        }

        // Get client IP 
        string clientIp = GetClientIpAddress(context);
        var now = DateTime.UtcNow;
        
        // Fast check - if this IP is in our blocked list, return 429 immediately 
        if (_blockedIps.TryGetValue(clientIp, out var blockInfo))
        {
            // If still blocked
            if (now < blockInfo.BlockedUntil)
            {
                // Only log if enough time has passed since last log for this IP
                bool shouldLog = (now - blockInfo.LastLogged) >= _logSuppressDuration;
                
                if (shouldLog)
                {
                    // Update last logged time
                    _blockedIps[clientIp] = (blockInfo.BlockedUntil, now);
                    
                    // Calculate remaining seconds
                    int remainingSeconds = (int)(blockInfo.BlockedUntil - now).TotalSeconds;
                    Log.Information("🚫 IP {IP} still rate limited for {Seconds} more seconds", clientIp, remainingSeconds);
                }
                
                // Always respond with 429, but don't always log
                int retryAfterSeconds = (int)(blockInfo.BlockedUntil - now).TotalSeconds;
                await RespondWithRateLimit(context, clientIp, retryAfterSeconds, shouldLog);
                return;
            }
            else
            {
                // No longer blocked, remove from dictionary
                _blockedIps.TryRemove(clientIp, out _);
                Log.Information("✅ Rate limit for IP {IP} has expired, allowing traffic", clientIp);
            }
        }
        
        // IP-based rate limiting
        string ipKey = $"ip:{clientIp}";
        var ipBucket = _buckets.GetOrAdd(ipKey, key => CreateBucket(key, _settings.IpLimit, _settings.IpWindow, "IP", tokenService));
        bool ipAllowed = ipBucket.TryConsume(1, _logger);

        if (!ipAllowed)
        {
            // Add to blocked IPs list
            var blockDuration = TimeSpan.FromSeconds(_settings.IpWindow);
            var blockedUntil = now.Add(blockDuration);
            _blockedIps[clientIp] = (blockedUntil, now); // Log this first violation
            
            Log.Warning("🚫 IP {IP} has exceeded rate limit, blocking for {Seconds}s", clientIp, _settings.IpWindow);
            await RespondWithRateLimit(context, clientIp, _settings.IpWindow, true);
            return;
        }
        
        bool requiresAuth = true;
        bool authenticated = false;
        string? token = null;
        TokenBucket? tokenBucket = null;
        
        if (context.Request.Headers.TryGetValue("Authorization", out var authHeader) && 
            authHeader.ToString().StartsWith("Bearer "))
        {
            token = authHeader.ToString().Substring("Bearer ".Length).Trim();
            authenticated = !string.IsNullOrEmpty(token);
        }
        
        if (!authenticated && requiresAuth)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            Log.Warning("❌ Missing or invalid authentication header from IP: {IP}", clientIp);

            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = "Authentication required",
                success = false
            }));
            return;
        }
        
        if (authenticated && token != null)
        {
            var maskedToken = MaskToken(token);
            string tokenKey = $"token:{token}";

            // Check if token is currently blocked
            if (_blockedTokens.TryGetValue(tokenKey, out var currentBlockInfo))
            {
                if (now < currentBlockInfo.BlockedUntil)
                {
                    // Token is still blocked
                    int remainingSeconds = (int)(currentBlockInfo.BlockedUntil - now).TotalSeconds;
                    
                    Log.Warning("🚫 Token {MaskedToken} is blocked for {Seconds} more seconds", 
                        maskedToken, remainingSeconds);
                    
                    await RespondWithRateLimit(context, maskedToken, remainingSeconds, true);
                    return;
                }
                else
                {
                    // Block expired, remove from blocked tokens
                    _blockedTokens.TryRemove(tokenKey, out _);
                }
            }
            
            tokenBucket = _buckets.GetOrAdd(tokenKey, key => 
                CreateBucket(key, _settings.TokenLimit, _settings.TokenWindow, "TOKEN", tokenService));
            
            bool tokenAllowed = tokenBucket.TryConsume(1, _logger);
            
            if (!tokenAllowed)
            {
                // Get or create block info
                var localBlockInfo = _blockedTokens.AddOrUpdate(
                    tokenKey,
                    _ => CreateBlockInfo(now),
                    (_, existing) => UpdateBlockInfo(existing, now)
                );

                // Use the calculated block duration
                var retryAfterSeconds = localBlockInfo.BlockDuration;

                // Logging with different levels based on block attempts
                if (localBlockInfo.ConsecutiveBlocks <= _maxConsecutiveBlocks)
                {
                    Log.Warning("🚫 Token rate limit exceeded for {MaskedToken} - Attempt {AttemptCount}", 
                        maskedToken, localBlockInfo.ConsecutiveBlocks);
                }
                else
                {
                    Log.Error("🛑 Persistent rate limit violations detected for {MaskedToken} - Blocking for {Seconds}s", 
                        maskedToken, retryAfterSeconds);
                }

                var retryTime = DateTimeOffset.UtcNow.AddSeconds(retryAfterSeconds).ToString("o");

                var errorObject = new
                {
                    error = localBlockInfo.ConsecutiveBlocks > _maxConsecutiveBlocks 
                        ? "Repeated rate limit violations" 
                        : "Too many requests",
                    retrytime = retryTime,
                    success = false
                };

                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.ContentType = "application/json";
                context.Response.Headers.Append("Retry-After", retryAfterSeconds.ToString());
                
                // Add rate limit headers
                AddRateLimitHeaders(context.Response, tokenBucket, "token");

                var options = new JsonSerializerOptions
                {
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                await context.Response.WriteAsync(JsonSerializer.Serialize(errorObject, options));
                return;
            }
            
            // Validate token
            bool isValidToken = await tokenService.VerifyTokenAsync(token);
            if (!isValidToken)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";

                Log.Debug("❌ Invalid token: {MaskedToken}", maskedToken);

                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    error = "Invalid token",
                    success = false
                }));
                return;
            }
        }
        
        // Add rate limit headers to successful responses
        context.Response.OnStarting(() =>
        {
            // Add rate limit headers based on the most restrictive bucket
            if (tokenBucket != null)
            {
                AddRateLimitHeaders(context.Response, tokenBucket, "token");
            }
            else
            {
                AddRateLimitHeaders(context.Response, ipBucket, "ip");
            }
            
            return Task.CompletedTask;
        });
        
        await _next(context);
    }

    private BlockInfo CreateBlockInfo(DateTime now)
    {
        return new BlockInfo
        {
            BlockedUntil = now.AddSeconds(_settings.TokenWindow),
            ConsecutiveBlocks = 1,
            BlockDuration = _settings.TokenWindow
        };
    }

    // Update block info with exponential backoff
    private BlockInfo UpdateBlockInfo(BlockInfo existing, DateTime now)
    {
        // Increment consecutive blocks
        int newConsecutiveBlocks = existing.ConsecutiveBlocks + 1;

        // Calculate new block duration with exponential backoff
        int newBlockDuration = newConsecutiveBlocks > _maxConsecutiveBlocks
            ? _settings.TokenWindow * (int)Math.Pow(2, newConsecutiveBlocks - _maxConsecutiveBlocks)
            : _settings.TokenWindow;

        // Cap the maximum block duration
        newBlockDuration = Math.Min(newBlockDuration, 3600); // Max 1 hour

        return new BlockInfo
        {
            BlockedUntil = now.AddSeconds(newBlockDuration),
            ConsecutiveBlocks = newConsecutiveBlocks,
            BlockDuration = newBlockDuration
        };
    }
    private void AddRateLimitHeaders(HttpResponse response, TokenBucket bucket, string resourceName)
    {
        int limit = bucket.GetCapacity();
        int remaining = bucket.GetRemainingTokens();
        long resetTimestamp = new DateTimeOffset(bucket.GetResetTime()).ToUnixTimeSeconds();
        int used = limit - remaining;
        
        response.Headers["X-RateLimit-Limit"] = limit.ToString();
        response.Headers["X-RateLimit-Remaining"] = remaining.ToString();
        response.Headers["X-RateLimit-Reset"] = resetTimestamp.ToString();
        response.Headers["X-RateLimit-Resource"] = resourceName;
        response.Headers["X-RateLimit-Used"] = used.ToString();
    }
    
    private async Task RespondWithRateLimit(HttpContext context, string identifier, int retryAfterSeconds, bool log)
    {
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.ContentType = "application/json";
        context.Response.Headers.Append("Retry-After", retryAfterSeconds.ToString());
        
        // Add rate limit headers if we have the bucket
        string bucketKey = identifier.StartsWith("token:") ? identifier : $"ip:{identifier}";
        if (_buckets.TryGetValue(bucketKey, out var bucket))
        {
            string resourceType = identifier.StartsWith("token:") ? "token" : "ip";
            AddRateLimitHeaders(context.Response, bucket, resourceType);
        }
        
        var retryTime = DateTimeOffset.UtcNow.AddSeconds(retryAfterSeconds).ToString("o");

        if (log)
        {
            Log.Warning("🚫 Rate limit enforced for {Identifier}, retry after {Seconds}s (at {Time})", 
                identifier, retryAfterSeconds, retryTime);
        }

        var errorObject = new
        {
            error = "Too many requests",
            retrytime = retryTime,
            success = false
        };

        var options = new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(errorObject, options));
    }
    private TokenBucket CreateBucket(string key, int limit, int windowSeconds, string type, TokenService? tokenService = null)
    {
        string displayKey = key;
        
        // For token buckets, try to get the token ID instead of showing the masked token
        if (type == "TOKEN" && tokenService != null && key.StartsWith("token:"))
        {
            // Check cache first
            if (_tokenDisplayCache.TryGetValue(key, out var cachedDisplay))
            {
                displayKey = cachedDisplay;
            }
            else
            {
                var token = key.Substring("token:".Length);
                try
                {
                    // Attempt to get token details synchronously for logging purposes
                    var tokenDetails = Task.Run(async () => await tokenService.GetTokenDetailsByTokenAsync(token)).Result;
                    if (tokenDetails != null)
                    {
                        displayKey = $"TOKEN_ID:{tokenDetails.Id} ({tokenDetails.Username})";
                        // Cache the result for future use
                        _tokenDisplayCache.TryAdd(key, displayKey);
                    }
                    else
                    {
                        displayKey = "UNKNOWN_TOKEN";
                        _tokenDisplayCache.TryAdd(key, displayKey);
                    }
                }
                catch
                {
                    // Fallback to masking if lookup fails
                    displayKey = MaskKey(key);
                    _tokenDisplayCache.TryAdd(key, displayKey);
                }
            }
        }
        else
        {
            // For IP buckets and fallback cases, use the masking function
            displayKey = MaskKey(key);
        }

        string MaskKey(string key, int visibleChars = 4, char maskChar = '*')
        {
            if (string.IsNullOrEmpty(key) || key.Length <= visibleChars)
                return key;
                
            return key.Substring(0, visibleChars) + new string(maskChar, key.Length - visibleChars);
        }

        _logger.LogInformation("🔧 Created {Type} rate limit bucket for {Key} with limit: {Limit}/{Window}s",
            type, displayKey, limit, windowSeconds);
            
        return new TokenBucket(
            limit,
            TimeSpan.FromSeconds(windowSeconds),
            key);
    }
    
    private string GetClientIpAddress(HttpContext context)
    {
        string? ip = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        
        if (string.IsNullOrEmpty(ip))
        {
            ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }
        else
        {
            ip = ip.Split(',').FirstOrDefault()?.Trim() ?? "unknown";
        }
        
        return ip;
    }
}

// Extension methods for adding RateLimiter middleware
public static class RateLimiterExtensions
{
    public static IApplicationBuilder UseRateLimiter(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<RateLimiter>();
    }
}

/// <summary>
/// Extension methods for Rate Limiting
/// </summary>
public static class RateLimitingExtensions
{
    /// <summary>
    /// Adds rate limiting configuration to the service collection
    /// </summary>
    public static IServiceCollection AddRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure RateLimitSettings from configuration
        var rateLimitSettings = new RateLimitSettings();
        configuration.GetSection("RateLimiting").Bind(rateLimitSettings);
        
        // Add settings as singleton for potential future DI
        services.AddSingleton(rateLimitSettings);
        
        return services;
    }

    /// <summary>
    /// Adds rate limiting middleware to the application pipeline
    /// </summary>
    public static IApplicationBuilder UseRateLimiter(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<RateLimiter>();
    }
}