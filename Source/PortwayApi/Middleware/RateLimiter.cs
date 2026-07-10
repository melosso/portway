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
using PortwayApi.Helpers;

public class RateLimiter
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed record BlockInfo(DateTime BlockedUntil, int ConsecutiveBlocks, int BlockDuration);

    private readonly RequestDelegate _next;
    private readonly RateLimitSettings _settings;
    private readonly Microsoft.Extensions.Logging.ILogger<RateLimiter> _logger;
    private readonly bool _uiAuthEnabled;
    private readonly string _adminApiKey;
    private readonly ConcurrentDictionary<string, TokenBucket> _buckets = new();
    private readonly ConcurrentDictionary<string, BlockInfo> _blockedTokens = new();
    private readonly string _instanceId = Guid.NewGuid().ToString()[..8];

    // Track blocked IPs with last logged time to prevent log flooding
    private readonly ConcurrentDictionary<string, (DateTime BlockedUntil, DateTime LastLogged)> _blockedIps = new();
    
    // Cache for token ID lookups to avoid repeated database calls
    private readonly ConcurrentDictionary<string, string> _tokenDisplayCache = new();
    private readonly Timer _cleanupTimer;
    
    // Configuration for block tracking
    private readonly int _maxConsecutiveBlocks = 3;
    private readonly TimeSpan _logCooldown = TimeSpan.FromSeconds(10);
    // Logging suppression duration for blocked IPs
    private readonly TimeSpan _logSuppressDuration = TimeSpan.FromSeconds(5);

    public RateLimiter(
        RequestDelegate next,
        IConfiguration configuration,
        Microsoft.Extensions.Logging.ILogger<RateLimiter> logger,
        string adminApiKey)
    {
        _next = next;
        _logger = logger;

        _settings = new RateLimitSettings();
        configuration.GetSection("RateLimiting").Bind(_settings);
        // Use the resolved Web UI admin key so the exemption check agrees with UseWebUiAuth
        _adminApiKey = adminApiKey ?? string.Empty;
        _uiAuthEnabled = !string.IsNullOrEmpty(_adminApiKey);
        
        if (_settings.Enabled)
        {
            _logger.LogInformation("Rate limiter {InstanceId} initialized. Enabled: {Enabled}, IP: {IpLimit}/{IpWindow}s, Token: {TokenLimit}/{TokenWindow}s",
            _instanceId, _settings.Enabled, _settings.IpLimit, _settings.IpWindow, _settings.TokenLimit, _settings.TokenWindow);
        }

        // Periodically remove expired entries to prevent unbounded dictionary growth
        _cleanupTimer = new Timer(_ => CleanupExpiredEntries(), null,
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    private void CleanupExpiredEntries()
    {
        var now = DateTime.UtcNow;

        foreach (var key in _blockedIps.Keys)
            if (_blockedIps.TryGetValue(key, out var v) && now >= v.BlockedUntil)
                _blockedIps.TryRemove(key, out _);

        foreach (var key in _blockedTokens.Keys)
            if (_blockedTokens.TryGetValue(key, out var b) && now >= b.BlockedUntil)
                _blockedTokens.TryRemove(key, out _);

        // Cap the display-name cache; entries are cheap to rebuild on next bucket creation
        if (_tokenDisplayCache.Count > 500)
            _tokenDisplayCache.Clear();

        Log.Debug("RateLimiter cleanup: {Buckets} buckets, {BlockedIps} blocked IPs, {BlockedTokens} blocked tokens",
            _buckets.Count, _blockedIps.Count, _blockedTokens.Count);
    }

    // Token masking method
    private string MaskToken(string token)
    {
        if (string.IsNullOrEmpty(token) || token.Length <= 8)
            return "token";

        return $"{token[..4]}...{token[^4..]}";
    }

    public async Task InvokeAsync(HttpContext context, AuthDbContext dbContext, TokenService tokenService)
    {
        var pathBase = context.Request.PathBase.Value ?? "";

        // Exempt authenticated admin UI requests from rate limiting
        // When auth is disabled, all /ui/* traffic is trusted
        // When auth is enabled, only requests that carry the session cookie are exempt -
        // unauthenticated paths (login page, auth API) still run through the rate limiter
        // to guard against brute-force attacks. The auth middleware upstream redirects
        // invalid cookies before they reach here, so cookie presence implies valid auth
        if (context.Request.Path.StartsWithSegments("/ui"))
        {
            // When auth is disabled all UI traffic is trusted
            if (!_uiAuthEnabled)
            {
                await _next(context);
                return;
            }

            // Never exempt the credential endpoints; the login and CSRF-token routes are the brute-force and token-flood surface
            bool isAuthPath = context.Request.Path.StartsWithSegments("/ui/api/auth") ||
                              context.Request.Path.StartsWithSegments("/ui/login");

            // Exempt only requests carrying a cryptographically valid session cookie, not merely a present one
            bool hasValidSession = context.Request.Cookies.TryGetValue("portway_auth", out var sessionCookie) &&
                                   WebUiAuthHelper.IsValidSessionCookie(sessionCookie, _adminApiKey);

            if (!isAuthPath && hasValidSession)
            {
                Log.Debug("Skipping rate limiting for authenticated UI request {Path}", context.Request.Path);
                await _next(context);
                return;
            }
        }

        if (context.Request.Path.StartsWithSegments("/openapi-docs") ||
            context.Request.Path.StartsWithSegments("/docs") ||
            context.Request.Path.StartsWithSegments("/health") ||
            context.Request.Path == "/" ||
            context.Request.Path == "/index.html" ||
            context.Request.Path.StartsWithSegments("/favicon.ico"))
        {
            Log.Debug("Skipping rate limiting for for {Path} (basePath: {pathBase})", context.Request.Path, pathBase);
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
                    Log.Information("IP {IP} still rate limited for {Seconds} more seconds", clientIp, remainingSeconds);
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
                Log.Information("Rate limit for IP {IP} has expired, allowing traffic", clientIp);
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
            
            Log.Warning("IP {IP} has exceeded rate limit, blocking for {Seconds}s", clientIp, _settings.IpWindow);
            await RespondWithRateLimit(context, clientIp, _settings.IpWindow, true);
            return;
        }
        
        // Extract token for per-token rate limiting (if present)
        // Auth enforcement is handled exclusively by TokenAuthMiddleware
        string? token = null;
        TokenBucket? tokenBucket = null;
        if (context.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var auth = authHeader.ToString();
            if (auth.StartsWith("Bearer ", StringComparison.Ordinal))
                token = auth["Bearer ".Length..].Trim();
        }

        if (token != null)
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
                    
                    Log.Warning("Token {MaskedToken} is blocked for {Seconds} more seconds", 
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
                    Log.Warning("Token rate limit exceeded for {MaskedToken} - Attempt {AttemptCount}", 
                        maskedToken, localBlockInfo.ConsecutiveBlocks);
                }
                else
                {
                    Log.Error("Persistent rate limit violations detected for {MaskedToken} - Blocking for {Seconds}s", 
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

                await context.Response.WriteAsync(JsonSerializer.Serialize(errorObject, _jsonOptions));
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
        return new BlockInfo(now.AddSeconds(_settings.TokenWindow), 1, _settings.TokenWindow);
    }

    // Update block info with exponential backoff
    private BlockInfo UpdateBlockInfo(BlockInfo existing, DateTime now)
    {
        int newConsecutiveBlocks = existing.ConsecutiveBlocks + 1;

        int newBlockDuration = newConsecutiveBlocks > _maxConsecutiveBlocks
            ? _settings.TokenWindow * (int)Math.Pow(2, newConsecutiveBlocks - _maxConsecutiveBlocks)
            : _settings.TokenWindow;

        newBlockDuration = Math.Min(newBlockDuration, 3600); // Max 1 hour

        return new BlockInfo(now.AddSeconds(newBlockDuration), newConsecutiveBlocks, newBlockDuration);
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
        string bucketKey = identifier.StartsWith("token:", StringComparison.Ordinal) ? identifier : $"ip:{identifier}";
        if (_buckets.TryGetValue(bucketKey, out var bucket))
        {
            string resourceType = identifier.StartsWith("token:", StringComparison.Ordinal) ? "token" : "ip";
            AddRateLimitHeaders(context.Response, bucket, resourceType);
        }
        
        var retryTime = DateTimeOffset.UtcNow.AddSeconds(retryAfterSeconds).ToString("o");

        if (log)
        {
            Log.Warning("Rate limit enforced for {Identifier}, retry after {Seconds}s (at {Time})", 
                identifier, retryAfterSeconds, retryTime);
        }

        var errorObject = new
        {
            error = "Too many requests",
            retrytime = retryTime,
            success = false
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(errorObject, _jsonOptions));
    }
        private TokenBucket CreateBucket(string key, int limit, int windowSeconds, string type, TokenService? tokenService = null)
    {
        string displayKey = key;
        
        // For token buckets, try to get the token ID instead of showing the masked token
        if (type == "TOKEN" && tokenService != null && key.StartsWith("token:", StringComparison.Ordinal))
        {
            // Check cache first
            if (_tokenDisplayCache.TryGetValue(key, out var cachedDisplay))
            {
                displayKey = cachedDisplay;
            }
            else
            {
                var token = key["token:".Length..];
                var maskedToken = MaskToken(token);
                displayKey = $"TOKEN:{maskedToken[..Math.Min(8, maskedToken.Length)]}...";
                _tokenDisplayCache.TryAdd(key, displayKey);
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
                
            return key[..visibleChars] + new string(maskChar, key.Length - visibleChars);
        }

        _logger.LogInformation("Created {Type} rate limit bucket for {Key} with limit: {Limit}/{Window}s",
            type, displayKey, limit, windowSeconds);
            
        return new TokenBucket(
            limit,
            TimeSpan.FromSeconds(windowSeconds),
            key);
    }
    
    // Use RemoteIpAddress set by UseForwardedHeaders middleware rather than reading
    // X-Forwarded-For directly; direct header reads are spoofable by clients
    private string GetClientIpAddress(HttpContext context)
    {
        var remoteIp = context.Connection.RemoteIpAddress;
        if (remoteIp == null) return "unknown";
        // Unwrap IPv4-mapped IPv6 addresses (e.g. ::ffff:192.168.1.1)
        if (remoteIp.IsIPv4MappedToIPv6)
            remoteIp = remoteIp.MapToIPv4();
        return remoteIp.ToString();
    }
}
