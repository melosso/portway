using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace PortwayApi.Helpers;

/// <summary>
/// Provides security hardening for Web UI (/ui) authentication including rate limiting, CSRF, and account lockout
/// </summary>
public static class WebUiAuthHelper
{
    // Rate limiting: max attempts per window
    private const int MaxAttemptsPerWindow = 10;
    private static readonly TimeSpan AttemptWindow = TimeSpan.FromMinutes(15);
    
    // Account lockout: lock after max failures, duration
    private const int MaxFailuresBeforeLockout = 10;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(30);

    // Track failed attempts: IP -> (failures, lockedUntil)
    private static readonly ConcurrentDictionary<string, (int Failures, DateTime? LockedUntil)> _failedAttempts = new();
    
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
    /// Checks if the client is rate limited or locked out
    /// </summary>
    /// <returns>Error message if blocked, null if allowed</returns>
    public static string? CheckAccess(string clientIp)
    {
        if (_failedAttempts.TryGetValue(clientIp, out var attempt))
        {
            // Check if currently locked out
            if (attempt.LockedUntil.HasValue && attempt.LockedUntil.Value > DateTime.UtcNow)
            {
                var remaining = attempt.LockedUntil.Value - DateTime.UtcNow;
                return $"Too many failed attempts. Try again in {(int)remaining.TotalMinutes} minutes.";
            }
            
            // Check if over rate limit
            if (attempt.Failures >= MaxAttemptsPerWindow)
            {
                return "Too many attempts. Please wait before trying again.";
            }
        }
        
        return null; // Allowed
    }

    /// <summary>
    /// Records a failed authentication attempt
    /// </summary>
    public static void RecordFailedAttempt(string clientIp)
    {
        var now = DateTime.UtcNow;
        
        _failedAttempts.AddOrUpdate(
            clientIp,
            // New entry
            _ => (1, null),
            // Existing entry
            (_, existing) =>
            {
                var failures = existing.LockedUntil.HasValue && existing.LockedUntil.Value < now
                    ? 1 // Reset after lockout expired
                    : existing.Failures + 1;
                
                // Lock out if too many failures
                DateTime? lockedUntil = failures >= MaxFailuresBeforeLockout
                    ? now.Add(LockoutDuration)
                    : null;
                
                return (failures, lockedUntil);
            });
    }

    /// <summary>
    /// Clears failed attempts after successful auth
    /// </summary>
    public static void ClearFailedAttempts(string clientIp)
    {
        _failedAttempts.TryRemove(clientIp, out _);
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
    /// <returns>True if valid</returns>
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
        var expiredAttempts = _failedAttempts.Where(kvp => 
            kvp.Value.LockedUntil.HasValue && kvp.Value.LockedUntil.Value < now).Select(kvp => kvp.Key).ToList();
        foreach (var ip in expiredAttempts)
        {
            _failedAttempts.TryRemove(ip, out _);
        }
    }
}
