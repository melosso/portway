using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace PortwayApi.Helpers;

/// <summary>
/// Provides security hardening for Web UI (/_) authentication including rate limiting, CSRF, and account lockout
/// </summary>
public static class WebUiAuthHelper
{
    // Account lockout: attempts per client and account, attempts per client across accounts, lock and counting window
    private const int MaxFailuresBeforeLockout = 10;
    private const int MaxFailuresPerClient = 50;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(30);

    // client ip, or client ip plus account, to failures in the current window
    private static readonly ConcurrentDictionary<string, (int Failures, DateTime? LockedUntil, DateTime WindowStart)> _failedAttempts = new();

    // Track CSRF tokens: token -> expiresAt
    private static readonly ConcurrentDictionary<string, DateTime> _csrfTokens = new();

    // Cleanup old entries periodically
    private static readonly Timer _cleanupTimer;

    static WebUiAuthHelper()
    {
        // Cleanup expired entries every 5 minutes
        _cleanupTimer = new Timer(_ => CleanupExpiredEntries(), null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    /// <summary>
    /// Counts an attempt for the client and the account before the password is checked; returns the block reason, or null when allowed. Call ClearFailedAttempts with the same arguments on success
    /// </summary>
    public static string? BeginAttempt(string clientIp, string account) =>
        Count(clientIp, MaxFailuresPerClient) ?? Count(AccountKey(clientIp, account), MaxFailuresBeforeLockout);

    /// <summary>
    /// Clears the account count and returns the attempt to the client count after successful auth
    /// </summary>
    public static void ClearFailedAttempts(string clientIp, string account)
    {
        _failedAttempts.TryRemove(AccountKey(clientIp, account), out _);
        _failedAttempts.AddOrUpdate(
            clientIp,
            _ => (0, null, DateTime.UtcNow),
            (_, existing) => existing.LockedUntil.HasValue ? existing : (Math.Max(0, existing.Failures - 1), null, existing.WindowStart));
    }

    // fixed size for untrusted names
    internal static string AccountKey(string clientIp, string account) =>
        $"{clientIp}\n{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(account.ToLowerInvariant())))}";

    private static string? Count(string key, int limit)
    {
        var now = DateTime.UtcNow;
        string? reason = null;

        // counted up front so parallel requests cannot all pass before the first failure is recorded
        _failedAttempts.AddOrUpdate(
            key,
            _ => (1, null, now),
            (_, existing) =>
            {
                reason = null;
                if (existing.LockedUntil > now)
                {
                    reason = $"Too many failed attempts. Try again in {(int)(existing.LockedUntil.Value - now).TotalMinutes} minutes.";
                    return existing;
                }

                var expired = existing.LockedUntil.HasValue || now - existing.WindowStart > LockoutDuration;
                var attempts = expired ? 1 : existing.Failures + 1;
                return (attempts, attempts >= limit ? now.Add(LockoutDuration) : null, expired ? now : existing.WindowStart);
            });

        return reason;
    }

    /// <summary>
    /// Signs a session cookie carrying the account id
    /// </summary>
    public static string IssueSessionCookie(int userId, int expiryHours)
    {
        var expiry = DateTimeOffset.UtcNow.AddHours(expiryHours).ToUnixTimeSeconds().ToString();
        return $"{userId}.{expiry}.{SignSession(userId, expiry)}";
    }

    /// <summary>
    /// The account id in a valid, unexpired session cookie, or null
    /// </summary>
    public static int? ResolveSession(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;

        var parts = token.Split('.');
        if (parts.Length != 3) return null;
        if (!int.TryParse(parts[0], out var userId)) return null;
        if (!long.TryParse(parts[1], out var expiry) ||
            DateTimeOffset.FromUnixTimeSeconds(expiry) < DateTimeOffset.UtcNow) return null;

        var expected = SignSession(userId, parts[1]);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(parts[2])) ? userId : null;
    }

    private static string SignSession(int userId, string expiry)
    {
        using var hmac = new HMACSHA256(SessionKeyProvider.Key);
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{userId}:{expiry}")));
    }

    /// <summary>
    /// Generates a new CSRF token
    /// </summary>
    public static string GenerateCsrfToken()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        _csrfTokens[token] = DateTime.UtcNow.AddHours(1);
        return token;
    }

    /// <summary>
    /// Validates a CSRF token
    /// </summary>
    public static bool ValidateCsrfToken(string? token)
    {
        if (string.IsNullOrEmpty(token))
            return false;

        if (_csrfTokens.TryGetValue(token, out var expiresAt))
        {
            if (expiresAt > DateTime.UtcNow)
            {
                return true;
            }
            // Remove expired token
            _csrfTokens.TryRemove(token, out _);
        }

        return false;
    }

    /// <summary>
    /// Removes a CSRF token after use (one-time use)
    /// </summary>
    public static void ConsumeCsrfToken(string token)
    {
        _csrfTokens.TryRemove(token, out _);
    }

    private static void CleanupExpiredEntries()
    {
        var now = DateTime.UtcNow;

        // Clean up CSRF tokens
        var expiredCsrf = _csrfTokens.Where(kvp => kvp.Value < now).Select(kvp => kvp.Key).ToList();
        foreach (var token in expiredCsrf)
        {
            _csrfTokens.TryRemove(token, out _);
        }

        // Clean up old failed attempts
        var expiredAttempts = _failedAttempts.Where(kvp => kvp.Value.LockedUntil is { } until
            ? until < now
            : now - kvp.Value.WindowStart > LockoutDuration).Select(kvp => kvp.Key).ToList();
        foreach (var key in expiredAttempts)
        {
            _failedAttempts.TryRemove(key, out _);
        }
    }
}
