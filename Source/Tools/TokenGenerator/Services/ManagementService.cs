using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Serilog;
using TokenGenerator.Classes;

namespace TokenGenerator.Services;

/// <summary>
/// Service for managing passphrase protection of the management tool
/// </summary>
public class ManagementService
{
    private readonly AuthDbContext _context;
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public ManagementService(AuthDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Check if passphrase has been set up
    /// </summary>
    public async Task<bool> IsPassphraseSetupAsync()
    {
        try
        {
            var management = await _context.Management.FirstOrDefaultAsync();
            return management != null && !string.IsNullOrEmpty(management.PassphraseHash);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error checking if passphrase is set up");
            return false;
        }
    }

    /// <summary>
    /// Check if the account is currently locked due to failed attempts
    /// </summary>
    public async Task<(bool IsLocked, TimeSpan? RemainingLockTime)> IsAccountLockedAsync()
    {
        try
        {
            var management = await _context.Management.FirstOrDefaultAsync();
            if (management?.LockedUntil != null && management.LockedUntil > DateTime.UtcNow)
            {
                var remaining = management.LockedUntil.Value - DateTime.UtcNow;
                return (true, remaining);
            }

            return (false, null);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error checking account lock status");
            return (false, null);
        }
    }

    /// <summary>
    /// Set up initial passphrase (only works if no passphrase exists)
    /// </summary>
    public async Task<(bool Success, string Message)> SetupPassphraseAsync(string passphrase)
    {
        try
        {
            // Check if passphrase already exists
            if (await IsPassphraseSetupAsync())
            {
                return (false, "Passphrase has already been set up. Use change passphrase option instead.");
            }

            // Validate passphrase strength
            var validation = ValidatePassphraseStrength(passphrase);
            if (!validation.IsValid)
            {
                return (false, validation.Message);
            }

            // Generate salt and hash
            var salt = GenerateSalt();
            var hash = HashPassphrase(passphrase, salt);

            // Create management record
            var management = new Management
            {
                PassphraseHash = hash,
                PassphraseSalt = salt,
                CreatedAt = DateTime.UtcNow,
                FailedAttempts = 0
            };

            _context.Management.Add(management);
            await _context.SaveChangesAsync();

            Log.Information("Passphrase successfully set up");
            return (true, "Passphrase has been successfully set up!");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error setting up passphrase");
            return (false, $"Failed to set up passphrase: {ex.Message}");
        }
    }

    /// <summary>
    /// Verify the provided passphrase
    /// </summary>
    public async Task<(bool Success, string Message)> VerifyPassphraseAsync(string passphrase)
    {
        try
        {
            var management = await _context.Management.FirstOrDefaultAsync();
            if (management == null)
            {
                return (false, "Passphrase has not been set up yet.");
            }

            // Check if account is locked
            var (isLocked, remainingTime) = await IsAccountLockedAsync();
            if (isLocked && remainingTime.HasValue)
            {
                var minutes = (int)remainingTime.Value.TotalMinutes;
                var seconds = remainingTime.Value.Seconds;
                return (false, $"Account is locked. Try again in {minutes}m {seconds}s.");
            }

            // Verify passphrase
            var hash = HashPassphrase(passphrase, management.PassphraseSalt);

            if (hash == management.PassphraseHash)
            {
                // Success - reset failed attempts
                management.FailedAttempts = 0;
                management.LastFailedAttempt = null;
                management.LockedUntil = null;
                await _context.SaveChangesAsync();

                Log.Debug("Passphrase verified successfully");
                
                return (true, "Authentication successful!");
            }
            else
            {
                // Failed attempt
                management.FailedAttempts++;
                management.LastFailedAttempt = DateTime.UtcNow;

                if (management.FailedAttempts >= MaxFailedAttempts)
                {
                    management.LockedUntil = DateTime.UtcNow.Add(LockoutDuration);
                    await _context.SaveChangesAsync();

                    Log.Warning("Account locked due to too many failed attempts");
                    return (false, $"Too many failed attempts. Account locked for {(int)LockoutDuration.TotalMinutes} minutes.");
                }

                await _context.SaveChangesAsync();

                var remainingAttempts = MaxFailedAttempts - management.FailedAttempts;
                Log.Warning("Failed passphrase attempt. Remaining attempts: {Remaining}", remainingAttempts);
                return (false, $"Invalid passphrase. {remainingAttempts} attempt(s) remaining before lockout.");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error verifying passphrase");
            return (false, $"Authentication error: {ex.Message}");
        }
    }

    /// <summary>
    /// Change existing passphrase (requires current passphrase)
    /// </summary>
    public async Task<(bool Success, string Message)> ChangePassphraseAsync(string currentPassphrase, string newPassphrase)
    {
        try
        {
            // Verify current passphrase
            var verification = await VerifyPassphraseAsync(currentPassphrase);
            if (!verification.Success)
            {
                return (false, "Current passphrase is incorrect.");
            }

            // Validate new passphrase strength
            var validation = ValidatePassphraseStrength(newPassphrase);
            if (!validation.IsValid)
            {
                return (false, validation.Message);
            }

            // Generate new salt and hash
            var salt = GenerateSalt();
            var hash = HashPassphrase(newPassphrase, salt);

            // Update management record
            var management = await _context.Management.FirstOrDefaultAsync();
            if (management == null)
            {
                return (false, "Management configuration not found.");
            }

            management.PassphraseHash = hash;
            management.PassphraseSalt = salt;
            management.UpdatedAt = DateTime.UtcNow;
            management.FailedAttempts = 0;

            await _context.SaveChangesAsync();

            Log.Information("Passphrase changed successfully");
            return (true, "Passphrase has been successfully changed!");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error changing passphrase");
            return (false, $"Failed to change passphrase: {ex.Message}");
        }
    }

    /// <summary>
    /// Generate a cryptographically secure salt
    /// </summary>
    private static string GenerateSalt()
    {
        var saltBytes = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(saltBytes);
        }
        return Convert.ToBase64String(saltBytes);
    }

    /// <summary>
    /// Hash a passphrase with salt using PBKDF2
    /// </summary>
    private static string HashPassphrase(string passphrase, string salt)
    {
        var saltBytes = Convert.FromBase64String(salt);
        using var pbkdf2 = new Rfc2898DeriveBytes(
            passphrase,
            saltBytes,
            iterations: 310000, // OWASP recommendation for PBKDF2-SHA256
            HashAlgorithmName.SHA256
        );
        var hash = pbkdf2.GetBytes(32);
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Validate passphrase strength
    /// </summary>
    private static (bool IsValid, string Message) ValidatePassphraseStrength(string passphrase)
    {
        if (string.IsNullOrWhiteSpace(passphrase))
        {
            return (false, "Passphrase cannot be empty.");
        }

        if (passphrase.Length < 12)
        {
            return (false, "Passphrase must be at least 12 characters long.");
        }

        bool hasUpper = passphrase.Any(char.IsUpper);
        bool hasLower = passphrase.Any(char.IsLower);
        bool hasDigit = passphrase.Any(char.IsDigit);
        bool hasSpecial = passphrase.Any(ch => !char.IsLetterOrDigit(ch));

        if (!hasUpper || !hasLower || !hasDigit || !hasSpecial)
        {
            return (false, "Passphrase must contain uppercase, lowercase, digit, and special character.");
        }

        return (true, "Passphrase is strong.");
    }
}