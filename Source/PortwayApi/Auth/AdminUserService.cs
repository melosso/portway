namespace PortwayApi.Auth;

using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Serilog;

/// <summary>Console accounts: hashing, verification and the one-time migration off WebUi:AdminApiKey</summary>
public class AdminUserService
{
    // PBKDF2-SHA256 above the OWASP minimum, in the same envelope Baseport uses
    private const int Iterations = 600_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public const int UsernameMax = 64;
    public const int PasswordMin = 12;
    public const int PasswordMax = 256;

    // No characters a person misreads off a log line
    private const string ReadableAlphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    private readonly AuthDbContext _db;

    public AdminUserService(AuthDbContext db) => _db = db;

    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        return $"pbkdf2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPassword(string password, string stored)
    {
        if (string.IsNullOrEmpty(stored) || password.Length > PasswordMax) return false;

        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2") return false;
        if (!int.TryParse(parts[1], out var iterations)) return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException) { return false; }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>Null when the name is acceptable, otherwise the reason it is not</summary>
    public static string? ValidateUsername(string? username)
    {
        if (string.IsNullOrWhiteSpace(username)) return "A username is required";
        if (username.Length > UsernameMax) return $"A username may be at most {UsernameMax} characters";
        // Deliberately narrow: a username appears in logs and audit lines, and must not be able to forge one
        if (!System.Text.RegularExpressions.Regex.IsMatch(username, @"^[a-zA-Z0-9._-]+$"))
            return "A username may contain letters, numbers, dots, hyphens and underscores";
        return null;
    }

    public static string? ValidatePassword(string? password)
    {
        if (string.IsNullOrEmpty(password)) return "A password is required";
        if (password.Length < PasswordMin) return $"A password must be at least {PasswordMin} characters";
        if (password.Length > PasswordMax) return $"A password may be at most {PasswordMax} characters";
        return null;
    }

    public async Task<List<AdminUser>> ListAsync()
    {
        return await _db.AdminUsers.OrderBy(u => u.Id).ToListAsync();
    }

    public async Task<AdminUser?> FindAsync(string username)
    {
        return await _db.AdminUsers.FirstOrDefaultAsync(u => u.Username == username);
    }

    public async Task<AdminUser?> FindByIdAsync(int id)
    {
        return await _db.AdminUsers.FirstOrDefaultAsync(u => u.Id == id);
    }

    public async Task<int> CountAsync()
    {
        return await _db.AdminUsers.CountAsync();
    }

    /// <summary>Verifies a sign-in and stamps the login time; null on any failure, with no reason leaked to the caller</summary>
    public async Task<AdminUser?> AuthenticateAsync(string username, string password)
    {
        var user = await _db.AdminUsers.FirstOrDefaultAsync(u => u.Username == username);

        // Hash regardless of whether the account exists, so a missing user and a wrong password cost the same
        var stored = user?.PasswordHash ?? HashPassword("no-such-account");
        var ok = VerifyPassword(password, stored);

        if (user is null || !ok || !user.IsActive) return null;

        user.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return user;
    }

    public async Task<AdminUser> CreateAsync(string username, string password, string role, string email = "")
    {
        var user = new AdminUser
        {
            Username = username,
            PasswordHash = HashPassword(password),
            Email = email.Trim(),
            Role = role,
            Provider = AdminUserProviders.Local,
        };
        _db.AdminUsers.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    public async Task<bool> UpdateAsync(int id, string? password, string? role, bool? isActive, string? email = null)
    {
        var user = await _db.AdminUsers.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return false;

        if (!string.IsNullOrEmpty(password)) user.PasswordHash = HashPassword(password);
        if (role is not null) user.Role = role;
        if (isActive is not null) user.IsActive = isActive.Value;
        if (email is not null) user.Email = email.Trim();

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var user = await _db.AdminUsers.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return false;

        _db.AdminUsers.Remove(user);
        await _db.SaveChangesAsync();
        return true;
    }

    /// <summary>True when this is the last account that can still administer the console</summary>
    public async Task<bool> IsLastAdministratorAsync(int id)
    {
        var others = await _db.AdminUsers.CountAsync(u =>
            u.Id != id && u.IsActive && u.Role == AdminUserRoles.Administrator);
        return others == 0;
    }

    /// <summary>
    /// Gives a new deployment an account to sign in with. An existing WebUi:AdminApiKey becomes that
    /// account's password so an upgrade stays reachable; otherwise one is generated and logged once.
    /// Either way the password must be changed at the first sign-in, because it was printed or sat in configuration.
    /// </summary>
    public async Task SeedFirstAccountAsync(string adminApiKey)
    {
        if (await _db.AdminUsers.AnyAsync())
        {
            if (!string.IsNullOrEmpty(adminApiKey))
                Log.Warning("WebUi:AdminApiKey is set but no longer used for sign-in; console accounts have replaced it and the setting can be removed");
            return;
        }

        var migrating = !string.IsNullOrEmpty(adminApiKey);

        // A migration keeps a predictable name because the operator already holds the key. A new deployment
        // does not, and a guessable operator name is half of every credential-stuffing attempt.
        var username = migrating ? "admin" : "admin-" + RandomNumberGenerator.GetString(ReadableAlphabet, 8);
        var password = migrating ? adminApiKey : RandomNumberGenerator.GetString(ReadableAlphabet, 24);

        _db.AdminUsers.Add(new AdminUser
        {
            Username = username,
            PasswordHash = HashPassword(password),
            Role = AdminUserRoles.Administrator,
            Provider = AdminUserProviders.Local,
            MustChangePassword = true,
        });
        await _db.SaveChangesAsync();

        if (migrating)
        {
            Log.Warning("Migrated WebUi:AdminApiKey into the console account {Username}; sign in with the key as its password, choose a new one, then remove the setting", username);
        }
        else
        {
            Log.Warning("Created the first console account. Sign in and choose a new password; this is the only time it is printed.");
            Log.Warning("    username: {Username}", username);
            Log.Warning("    password: {Password}", password);
        }
    }

    /// <summary>
    /// Confirms the signed-in account's own password. A session is not enough to change who can sign
    /// in: a borrowed one must not be able to add a way back in, or take somebody else's away.
    /// </summary>
    public async Task<bool> ConfirmPasswordAsync(int userId, string password)
    {
        var user = await _db.AdminUsers.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null || !user.IsActive) return false;

        // A federated account has no password to confirm with; it must set one before it can manage accounts
        if (user.PasswordHash.Length == 0) return false;

        return VerifyPassword(password, user.PasswordHash);
    }

    /// <summary>Replaces a password the account did not choose; verifies the current one so a borrowed session cannot do it</summary>
    public async Task<bool> ChangePasswordAsync(string username, string currentPassword, string newPassword)
    {
        var user = await _db.AdminUsers.FirstOrDefaultAsync(u => u.Username == username);
        if (user is null || !user.IsActive || !VerifyPassword(currentPassword, user.PasswordHash)) return false;

        user.PasswordHash = HashPassword(newPassword);
        user.MustChangePassword = false;
        user.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }
}
