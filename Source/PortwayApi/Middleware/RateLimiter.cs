namespace PortwayApi.Middleware;

using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Serilog;
using Microsoft.Extensions.DependencyInjection;
using PortwayApi.Auth;
using PortwayApi.Helpers;

public class RateLimiter
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly RequestDelegate _next;
    private readonly RateLimitSettings _settings;
    private readonly IRateLimiterStore _store;
    private readonly RateLimiterState _state;
    private readonly TimeProvider _timeProvider;
    private readonly Microsoft.Extensions.Logging.ILogger<RateLimiter> _logger;
    private readonly bool _uiAuthEnabled;
    private readonly string _adminApiKey;
    private readonly string? _metricsPath;
    private readonly string _instanceId = Guid.NewGuid().ToString()[..8];

    private readonly ITimer _cleanupTimer;

    // Configuration for block tracking
    private readonly int _maxConsecutiveBlocks = 3;
    // Logging suppression duration for blocked IPs
    private readonly TimeSpan _logSuppressDuration = TimeSpan.FromSeconds(5);

    public RateLimiter(
        RequestDelegate next,
        RateLimitSettings settings,
        IRateLimiterStore store,
        RateLimiterState state,
        TimeProvider timeProvider,
        Microsoft.Extensions.Logging.ILogger<RateLimiter> logger,
        Services.Telemetry.TelemetryOptions telemetryOptions,
        string adminApiKey)
    {
        _next = next;

        // Monitoring scrapers must never be throttled; the endpoint is restricted at the network level
        _metricsPath = telemetryOptions.ActiveMetricsPath;
        _settings = settings;
        _store = store;
        _state = state;
        _timeProvider = timeProvider;
        _logger = logger;

        // Use the resolved Web UI admin key so the exemption check agrees with UseWebUiAuth
        _adminApiKey = adminApiKey ?? string.Empty;
        _uiAuthEnabled = !string.IsNullOrEmpty(_adminApiKey);

        if (_settings.Enabled)
        {
            _logger.LogInformation("Rate limiter {InstanceId} initialized. Enabled: {Enabled}, Store: {Store}, IP: {IpLimit}/{IpWindow}s, Token: {TokenLimit}/{TokenWindow}s",
                _instanceId, _settings.Enabled, _store.GetType().Name, _settings.IpLimit, _settings.IpWindow, _settings.TokenLimit, _settings.TokenWindow);
        }

        // Periodically remove expired entries to prevent unbounded dictionary growth
        _cleanupTimer = timeProvider.CreateTimer(_ => CleanupExpiredEntries(), null,
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    private void CleanupExpiredEntries()
    {
        var now = _timeProvider.GetUtcNow();

        foreach (var key in _state.BlockedIps.Keys)
            if (_state.BlockedIps.TryGetValue(key, out var v) && now >= v.BlockedUntil)
                _state.BlockedIps.TryRemove(key, out _);

        foreach (var key in _state.BlockedTokens.Keys)
            if (_state.BlockedTokens.TryGetValue(key, out var b) && now >= b.BlockedUntil)
                _state.BlockedTokens.TryRemove(key, out _);

        Log.Debug("RateLimiter cleanup: {BlockedIps} blocked IPs, {BlockedTokens} blocked tokens",
            _state.BlockedIps.Count, _state.BlockedTokens.Count);
    }

    // Token masking method
    private string MaskToken(string token)
    {
        if (string.IsNullOrEmpty(token) || token.Length <= 8)
            return "token";

        return $"{token[..4]}...{token[^4..]}";
    }

    public async Task InvokeAsync(HttpContext context)
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

        if (context.Request.Path.StartsWithSegments("/docs") ||
            context.Request.Path.StartsWithSegments("/health") ||
            (_metricsPath is not null && context.Request.Path.StartsWithSegments(_metricsPath)) ||
            context.Request.Path == "/" ||
            context.Request.Path == "/index.html" ||
            context.Request.Path.StartsWithSegments("/favicon.ico"))
        {
            Log.Debug("Skipping rate limiting for {Path} (basePath: {pathBase})", context.Request.Path, pathBase);
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
        var now = _timeProvider.GetUtcNow();

        // Fast check - if this IP is in our blocked list, return 429 immediately
        if (_state.BlockedIps.TryGetValue(clientIp, out var blockInfo))
        {
            // If still blocked
            if (now < blockInfo.BlockedUntil)
            {
                // Only log if enough time has passed since last log for this IP
                bool shouldLog = (now - blockInfo.LastLogged) >= _logSuppressDuration;

                if (shouldLog)
                {
                    // Update last logged time
                    _state.BlockedIps[clientIp] = (blockInfo.BlockedUntil, now);

                    // Calculate remaining seconds
                    int remainingSeconds = (int)(blockInfo.BlockedUntil - now).TotalSeconds;
                    Log.Information("IP {IP} still rate limited for {Seconds} more seconds", clientIp, remainingSeconds);
                }

                // Always respond with 429, but don't always log
                int retryAfterSeconds = (int)(blockInfo.BlockedUntil - now).TotalSeconds;
                await RespondWithRateLimit(context, clientIp, retryAfterSeconds, _settings.IpLimit, "ip", shouldLog);
                return;
            }
            else
            {
                // No longer blocked, remove from dictionary
                _state.BlockedIps.TryRemove(clientIp, out _);
                Log.Information("Rate limit for IP {IP} has expired, allowing traffic", clientIp);
            }
        }

        // IP-based rate limiting
        var ipLease = await _store.TryConsumeAsync($"ip:{clientIp}", _settings.IpLimit, _settings.IpWindow, context.RequestAborted);

        if (!ipLease.Allowed)
        {
            // Deliberate penalty box: an overflowing IP is blocked for the full window as a deterrent
            var blockedUntil = now.AddSeconds(_settings.IpWindow);
            _state.BlockedIps[clientIp] = (blockedUntil, now); // Log this first violation

            Log.Warning("IP {IP} has exceeded rate limit, blocking for {Seconds}s", clientIp, _settings.IpWindow);
            await RespondWithRateLimit(context, clientIp, _settings.IpWindow, _settings.IpLimit, "ip", true);
            return;
        }

        // Extract token for per-token rate limiting (if present)
        // Auth enforcement is handled exclusively by TokenAuthMiddleware
        string? token = null;
        RateLimitLease? tokenLease = null;
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

            // Per-token override from the token record, null falls back to global settings
            // Lookup is cache-backed (30s TTL) so this does not hash per request
            var (effectiveLimit, effectiveWindow) = await ResolveTokenLimitsAsync(context, token);

            // Check if token is currently blocked
            if (_state.BlockedTokens.TryGetValue(tokenKey, out var currentBlockInfo))
            {
                if (now < currentBlockInfo.BlockedUntil)
                {
                    // Token is still blocked
                    int remainingSeconds = (int)(currentBlockInfo.BlockedUntil - now).TotalSeconds;

                    Log.Warning("Token {MaskedToken} is blocked for {Seconds} more seconds",
                        maskedToken, remainingSeconds);

                    await RespondWithRateLimit(context, maskedToken, remainingSeconds, effectiveLimit, "token", true);
                    return;
                }
                else
                {
                    // Block expired, remove from blocked tokens
                    _state.BlockedTokens.TryRemove(tokenKey, out _);
                }
            }

            // Limit and window are part of the bucket key so a changed override starts a fresh bucket
            var lease = await _store.TryConsumeAsync($"{tokenKey}:{effectiveLimit}:{effectiveWindow}", effectiveLimit, effectiveWindow, context.RequestAborted);
            tokenLease = lease;

            if (!lease.Allowed)
            {
                var localBlockInfo = _state.BlockedTokens.AddOrUpdate(
                    tokenKey,
                    _ => CreateBlockInfo(now, effectiveWindow),
                    (_, existing) => UpdateBlockInfo(existing, now, effectiveWindow)
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

                var retryTime = now.AddSeconds(retryAfterSeconds).ToString("o");

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
                AddRateLimitHeaders(context.Response, BlockedLease(effectiveLimit, now, retryAfterSeconds), "token");

                await context.Response.WriteAsync(JsonSerializer.Serialize(errorObject, _jsonOptions));
                return;
            }
        }

        // Add rate limit headers to successful responses
        context.Response.OnStarting(() =>
        {
            // Report the most restrictive lease: token when present, IP otherwise
            if (tokenLease is { } tl)
                AddRateLimitHeaders(context.Response, tl, "token");
            else
                AddRateLimitHeaders(context.Response, ipLease, "ip");

            return Task.CompletedTask;
        });

        await _next(context);
    }

    // Resolves the effective token limit from the token record, global settings when absent
    private async Task<(int Limit, int Window)> ResolveTokenLimitsAsync(HttpContext context, string token)
    {
        try
        {
            // Optional so unit tests and stripped-down hosts without auth services still work
            var tokenService = context.RequestServices?.GetService<TokenService>();
            if (tokenService != null)
            {
                var details = await tokenService.GetTokenDetailsByTokenAsync(token);
                if (details?.RateLimitRequests is int requests and > 0)
                    return (requests, details.RateLimitWindowSeconds is int w and > 0 ? w : _settings.TokenWindow);
            }
        }
        catch (Exception ex)
        {
            // Never let a lookup failure take rate limiting down, fall back to globals
            Log.Warning(ex, "Per-token rate limit lookup failed, using global limits");
        }

        return (_settings.TokenLimit, _settings.TokenWindow);
    }

    private TokenBlockInfo CreateBlockInfo(DateTimeOffset now, int windowSeconds)
    {
        return new TokenBlockInfo(now.AddSeconds(windowSeconds), 1, windowSeconds);
    }

    // Update block info with exponential backoff
    private TokenBlockInfo UpdateBlockInfo(TokenBlockInfo existing, DateTimeOffset now, int windowSeconds)
    {
        int newConsecutiveBlocks = existing.ConsecutiveBlocks + 1;

        int newBlockDuration = newConsecutiveBlocks > _maxConsecutiveBlocks
            ? windowSeconds * (int)Math.Pow(2, newConsecutiveBlocks - _maxConsecutiveBlocks)
            : windowSeconds;

        newBlockDuration = Math.Min(newBlockDuration, 3600); // Max 1 hour

        return new TokenBlockInfo(now.AddSeconds(newBlockDuration), newConsecutiveBlocks, newBlockDuration);
    }

    // Header values for a blocked identifier: nothing remains, reset when the block lifts
    private static RateLimitLease BlockedLease(int limit, DateTimeOffset now, int retryAfterSeconds)
        => new(false, limit, 0, now.AddSeconds(retryAfterSeconds).ToUnixTimeSeconds());

    private static void AddRateLimitHeaders(HttpResponse response, RateLimitLease lease, string resourceName)
    {
        response.Headers["X-RateLimit-Limit"] = lease.Limit.ToString();
        response.Headers["X-RateLimit-Remaining"] = lease.Remaining.ToString();
        response.Headers["X-RateLimit-Reset"] = lease.ResetUnixSeconds.ToString();
        response.Headers["X-RateLimit-Resource"] = resourceName;
        response.Headers["X-RateLimit-Used"] = (lease.Limit - lease.Remaining).ToString();
    }

    private async Task RespondWithRateLimit(HttpContext context, string identifier, int retryAfterSeconds, int limit, string resourceName, bool log)
    {
        var now = _timeProvider.GetUtcNow();

        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.ContentType = "application/json";
        context.Response.Headers.Append("Retry-After", retryAfterSeconds.ToString());
        AddRateLimitHeaders(context.Response, BlockedLease(limit, now, retryAfterSeconds), resourceName);

        var retryTime = now.AddSeconds(retryAfterSeconds).ToString("o");

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
