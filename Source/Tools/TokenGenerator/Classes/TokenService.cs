using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Serilog;

namespace TokenGenerator.Classes;

public class TokenService
{
    private readonly AuthDbContext _dbContext;
    private readonly string _tokenFolderPath;
    private readonly ILogger<TokenService> _logger;

    public TokenService(AuthDbContext dbContext, AppConfig config, ILogger<TokenService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
        
        // Ensure tokens directory exists
        _tokenFolderPath = !Path.IsPathRooted(config.TokensFolder) 
            ? Path.GetFullPath(config.TokensFolder) 
            : config.TokensFolder;
        
        if (!Directory.Exists(_tokenFolderPath))
        {
            Directory.CreateDirectory(_tokenFolderPath);
            Log.Information("Created tokens directory at {Path}", _tokenFolderPath);
        }
    }
    
    public async Task<string> GenerateTokenAsync(
        string username, 
        string allowedScopes = "*",
        string allowedEnvironments = "*", 
        string description = "",
        int? expiresInDays = null)
    {
        try
        {
            // Check if a token for this username already exists
            string tokenFilePath = Path.Combine(_tokenFolderPath, $"{username}.txt");
            if (File.Exists(tokenFilePath))
            {
                Log.Warning("A token file for user '{Username}' already exists at '{Path}'", username, tokenFilePath);
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"WARNING: A token file for user '{username}' already exists.");
                Console.WriteLine($"Location: {tokenFilePath}");
                Console.WriteLine("Do you want to generate a new token and overwrite the existing file? (y/n)");
                Console.ResetColor();
                
                string? response = Console.ReadLine()?.Trim().ToLower();
                if (response != "y" && response != "yes")
                {
                    Log.Information("Token generation canceled by user");
                    throw new OperationCanceledException("Token generation canceled by user");
                }
                
                Log.Information("User confirmed overwriting existing token file");
            }
            
            // Generate a secure random token
            string token = GenerateSecureToken();
            
            // Generate salt for hashing
            byte[] salt = GenerateSalt();
            string saltString = Convert.ToBase64String(salt);
            
            // Hash the token
            string hashedToken = HashToken(token, salt);

            // Calculate expiration if specified
            DateTime? expiresAt = expiresInDays.HasValue 
                ? DateTime.UtcNow.AddDays(expiresInDays.Value) 
                : null;
            
            // Create a new token entry
            var tokenEntry = new AuthToken
            {
                Username = username,
                TokenHash = hashedToken,
                TokenSalt = saltString,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = expiresAt,
                AllowedScopes = allowedScopes,
                AllowedEnvironments = allowedEnvironments,
                Description = description
            };
            
            // Add to database using async methods
            await _dbContext.Tokens.AddAsync(tokenEntry);
            await _dbContext.SaveChangesAsync();
            
            // Log the token creation in audit trail
            await LogAuditAsync(tokenEntry.Id, username, "Created", null, hashedToken, 
                JsonSerializer.Serialize(new 
                { 
                    AllowedScopes = allowedScopes,
                    AllowedEnvironments = allowedEnvironments,
                    Description = description,
                    ExpiresAt = expiresAt?.ToString("yyyy-MM-dd HH:mm:ss"),
                    TokenLength = token.Length
                }));
            
            // Save token to file
            await SaveTokenToFileAsync(username, token, allowedScopes, allowedEnvironments, expiresAt, description);
            
            return token;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating token for user {Username}", username);
            throw;
        }
    }

    private static string GenerateSecureToken()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        return RandomNumberGenerator.GetString(chars, 128);
    }

    public async Task<List<AuthToken>> GetActiveTokensAsync()
    {
        try
        {
            return await _dbContext.Tokens
                .Where(t => t.RevokedAt == null && (t.ExpiresAt == null || t.ExpiresAt > DateTime.UtcNow))
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving active tokens");
            return new List<AuthToken>();
        }
    }

    public async Task<List<AuthToken>> GetAllTokensAsync()
    {
        try
        {
            return await _dbContext.Tokens
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all tokens");
            return new List<AuthToken>();
        }
    }

    public async Task<AuthToken?> GetTokenByIdAsync(int id)
    {
        try
        {
            return await _dbContext.Tokens.FindAsync(id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving token with ID {Id}", id);
            return null;
        }
    }

    public async Task<bool> RevokeTokenAsync(int id)
    {
        try
        {
            var token = await _dbContext.Tokens.FindAsync(id);
            if (token == null)
                return false;
                
            // Store old hash for audit
            string oldTokenHash = token.TokenHash;
            
            // Update the RevokedAt timestamp instead of deleting
            token.RevokedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
            
            // Log the token revocation in audit trail
            await LogAuditAsync(token.Id, token.Username, "Revoked", oldTokenHash, null,
                JsonSerializer.Serialize(new 
                { 
                    RevokedAt = token.RevokedAt?.ToString("yyyy-MM-dd HH:mm:ss"),
                    Reason = "Manual revocation via TokenGenerator"
                }));
            
            // Delete the token file if it exists
            string filePath = Path.Combine(_tokenFolderPath, $"{token.Username}.txt");
            if (File.Exists(filePath))
            {
                try 
                {
                    File.Delete(filePath);
                    Log.Information("Deleted token file for {Username}", token.Username);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Could not delete token file for {Username}", token.Username);
                }
            }
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revoking token with ID {Id}", id);
            return false;
        }
    }
    
    public async Task<bool> UpdateTokenScopesAsync(int id, string newScopes)
    {
        try
        {
            var token = await _dbContext.Tokens.FindAsync(id);
            if (token == null)
                return false;
                
            string oldScopes = token.AllowedScopes;
            token.AllowedScopes = newScopes;
            await _dbContext.SaveChangesAsync();
            
            // Log the token scope update in audit trail
            await LogAuditAsync(token.Id, token.Username, "ScopesUpdated", null, null,
                JsonSerializer.Serialize(new 
                { 
                    OldScopes = oldScopes,
                    NewScopes = newScopes,
                    UpdatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
                }));
            
            // Update token file if it exists
            await UpdateTokenFileAsync(token);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating token scopes for ID {Id}", id);
            return false;
        }
    }

    public async Task<bool> UpdateTokenEnvironmentsAsync(int id, string newEnvironments)
    {
        try
        {
            var token = await _dbContext.Tokens.FindAsync(id);
            if (token == null)
                return false;
                
            string oldEnvironments = token.AllowedEnvironments;
            token.AllowedEnvironments = newEnvironments;
            await _dbContext.SaveChangesAsync();
            
            // Log the token environment update in audit trail
            await LogAuditAsync(token.Id, token.Username, "EnvironmentsUpdated", null, null,
                JsonSerializer.Serialize(new 
                { 
                    OldEnvironments = oldEnvironments,
                    NewEnvironments = newEnvironments,
                    UpdatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
                }));
            
            // Update token file if it exists
            await UpdateTokenFileAsync(token);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating token environments for ID {Id}", id);
            return false;
        }
    }

    public async Task<bool> UpdateTokenExpirationAsync(int id, int? daysValid)
    {
        try
        {
            var token = await _dbContext.Tokens.FindAsync(id);
            if (token == null)
                return false;
                
            // Calculate new expiration
            DateTime? oldExpiresAt = token.ExpiresAt;
            DateTime? expiresAt = daysValid.HasValue 
                ? DateTime.UtcNow.AddDays(daysValid.Value) 
                : null;
                
            token.ExpiresAt = expiresAt;
            await _dbContext.SaveChangesAsync();
            
            // Log the token expiration update in audit trail
            await LogAuditAsync(token.Id, token.Username, "ExpirationUpdated", null, null,
                JsonSerializer.Serialize(new 
                { 
                    OldExpiresAt = oldExpiresAt?.ToString("yyyy-MM-dd HH:mm:ss"),
                    NewExpiresAt = expiresAt?.ToString("yyyy-MM-dd HH:mm:ss"),
                    DaysValid = daysValid
                }));
            
            // Update token file if it exists
            await UpdateTokenFileAsync(token);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating token expiration for ID {Id}", id);
            return false;
        }
    }

    public async Task<string> RotateTokenAsync(int id)
    {
        try
        {
            Log.Information("Starting token rotation for ID {TokenId}", id);
            
            // Get the existing token
            var existingToken = await _dbContext.Tokens.FindAsync(id);
            if (existingToken == null)
            {
                Log.Warning("Token with ID {TokenId} not found for rotation", id);
                throw new InvalidOperationException($"Token with ID {id} not found");
            }
            
            if (!existingToken.IsActive)
            {
                Log.Warning("Cannot rotate inactive token with ID {TokenId}", id);
                throw new InvalidOperationException($"Cannot rotate inactive token with ID {id}");
            }
            
            // Store old token information for audit
            string oldTokenHash = existingToken.TokenHash;
            
            // Log rotation start
            await LogAuditAsync(existingToken.Id, existingToken.Username, "RotationStarted", oldTokenHash, null,
                JsonSerializer.Serialize(new 
                { 
                    StartTime = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                    Reason = "Manual rotation via TokenGenerator"
                }));
            
            // Generate new token
            string newToken = GenerateSecureToken();
            byte[] newSalt = GenerateSalt();
            string newSaltString = Convert.ToBase64String(newSalt);
            string newHashedToken = HashToken(newToken, newSalt);
            
            Log.Information("Generated new token for rotation of user {Username}", existingToken.Username);
            
            // Begin transaction for atomic operation
            using var transaction = await _dbContext.Database.BeginTransactionAsync();
            
            try
            {
                // Revoke the existing token
                existingToken.RevokedAt = DateTime.UtcNow;
                
                // Create new token with same permissions
                var newTokenEntry = new AuthToken
                {
                    Username = existingToken.Username,
                    TokenHash = newHashedToken,
                    TokenSalt = newSaltString,
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = existingToken.ExpiresAt, // Keep same expiration
                    AllowedScopes = existingToken.AllowedScopes,
                    AllowedEnvironments = existingToken.AllowedEnvironments,
                    Description = existingToken.Description.EndsWith(" (Rotated)") 
                        ? existingToken.Description 
                        : existingToken.Description + " (Rotated)"
                };
                
                await _dbContext.Tokens.AddAsync(newTokenEntry);
                await _dbContext.SaveChangesAsync();
                
                // Log successful rotation
                await LogAuditAsync(newTokenEntry.Id, existingToken.Username, "Rotated", oldTokenHash, newHashedToken,
                    JsonSerializer.Serialize(new 
                    { 
                        OldTokenId = existingToken.Id,
                        NewTokenId = newTokenEntry.Id,
                        RotatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                        AllowedScopes = existingToken.AllowedScopes,
                        AllowedEnvironments = existingToken.AllowedEnvironments,
                        ExpiresAt = existingToken.ExpiresAt?.ToString("yyyy-MM-dd HH:mm:ss")
                    }));
                
                // Update the token file with new token
                string rotatedDescription = existingToken.Description.EndsWith(" (Rotated)") 
                    ? existingToken.Description 
                    : existingToken.Description + " (Rotated)";
                    
                await SaveTokenToFileAsync(
                    existingToken.Username, 
                    newToken, 
                    existingToken.AllowedScopes,
                    existingToken.AllowedEnvironments,
                    existingToken.ExpiresAt, 
                    rotatedDescription);
                
                // Commit transaction
                await transaction.CommitAsync();
                
                Log.Information("Token rotation completed successfully for user {Username}. Old ID: {OldId}, New ID: {NewId}", 
                    existingToken.Username, existingToken.Id, newTokenEntry.Id);
                
                return newToken;
            }
            catch (Exception ex)
            {
                // Rollback transaction on error
                await transaction.RollbackAsync();
                
                // Log rollback
                await LogAuditAsync(existingToken.Id, existingToken.Username, "RotationFailed", oldTokenHash, null,
                    JsonSerializer.Serialize(new 
                    { 
                        Error = ex.Message,
                        FailedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                        Action = "Transaction rolled back"
                    }));
                
                Log.Error(ex, "Token rotation failed for user {Username}, transaction rolled back", existingToken.Username);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rotating token with ID {Id}", id);
            throw;
        }
    }

    public async Task<bool> VerifyTokenAsync(string token)
    {
        try
        {
            // Get active tokens
            var tokens = await _dbContext.Tokens
                .Where(t => t.RevokedAt == null && (t.ExpiresAt == null || t.ExpiresAt > DateTime.UtcNow))
                .ToListAsync();
            
            // Check each token
            foreach (var storedToken in tokens)
            {
                // Convert stored salt from string to bytes
                byte[] salt = Convert.FromBase64String(storedToken.TokenSalt);
                
                // Hash the provided token with the stored salt
                string hashedToken = HashToken(token, salt);
                
                // Compare hashed tokens
                if (hashedToken == storedToken.TokenHash)
                {
                    return true;
                }
            }
            
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying token");
            return false;
        }
    }

    public string GetTokenFolderPath()
    {
        return _tokenFolderPath;
    }

    private async Task LogAuditAsync(int? tokenId, string username, string operation, 
        string? oldTokenHash = null, string? newTokenHash = null, string details = "")
    {
        try
        {
            var auditEntry = new AuthTokenAudit
            {
                TokenId = tokenId,
                Username = username,
                Operation = operation,
                OldTokenHash = oldTokenHash,
                NewTokenHash = newTokenHash,
                Timestamp = DateTime.UtcNow,
                Details = details,
                Source = "TokenGenerator",
                IpAddress = null, // Could be enhanced to capture actual IP
                UserAgent = Environment.MachineName + "/" + Environment.UserName
            };
            
            await _dbContext.TokenAudits.AddAsync(auditEntry);
            await _dbContext.SaveChangesAsync();
            
            Log.Debug("Audit log created: {Operation} for user {Username}", operation, username);
        }
        catch (Exception ex)
        {
            // Don't throw exceptions from audit logging to avoid disrupting main operations
            Log.Error(ex, "Failed to create audit log for operation {Operation} on user {Username}", operation, username);
        }
    }

    private async Task UpdateTokenFileAsync(AuthToken token)
    {
        string filePath = Path.Combine(_tokenFolderPath, $"{token.Username}.txt");
        if (File.Exists(filePath))
        {
            try
            {
                // Read the existing token file to preserve the token value
                string jsonContent = await File.ReadAllTextAsync(filePath);
                var tokenInfo = JsonSerializer.Deserialize<TokenFileInfo>(jsonContent);
                
                if (tokenInfo != null)
                {
                    await SaveTokenToFileAsync(
                        token.Username, 
                        tokenInfo.Token, 
                        token.AllowedScopes,
                        token.AllowedEnvironments,
                        token.ExpiresAt, 
                        token.Description);
                        
                    Log.Information("Updated token file for {Username}", token.Username);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not update token file for {Username}", token.Username);
            }
        }
    }
    
    private static string HashToken(string token, byte[] salt)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(token, salt, 10000, HashAlgorithmName.SHA256);
        byte[] hash = pbkdf2.GetBytes(32); // 256 bits
        return Convert.ToBase64String(hash);
    }
    
    private static byte[] GenerateSalt()
    {
        byte[] salt = new byte[16];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(salt);
        return salt;
    }
    
    private async Task SaveTokenToFileAsync(
        string username, 
        string token, 
        string allowedScopes = "*", 
        string allowedEnvironments = "*",
        DateTime? expiresAt = null,
        string description = "")
    {
        try
        {
            // Ensure tokens directory exists
            if (!Directory.Exists(_tokenFolderPath))
            {
                Directory.CreateDirectory(_tokenFolderPath);
                Log.Information("Created tokens directory at {Path}", _tokenFolderPath);
            }
            
            string filePath = Path.Combine(_tokenFolderPath, $"{username}.txt");
            
            // Create a simple token file
            var tokenInfo = new TokenFileInfo
            {
                Username = username,
                Token = token,
                AllowedScopes = allowedScopes,
                AllowedEnvironments = allowedEnvironments,
                ExpiresAt = expiresAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Never",
                CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                Usage = "Use this token in the Authorization header as: Bearer <token>"
            };
            
            // Only include Description if it's not empty
            if (!string.IsNullOrEmpty(description))
            {
                tokenInfo.Description = description;
            }
            
            var options = new JsonSerializerOptions { WriteIndented = true };
            string tokenJson = JsonSerializer.Serialize(tokenInfo, options);
            await File.WriteAllTextAsync(filePath, tokenJson);
            
            Log.Information("Token file saved to {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save token file for user {Username} at {Path}", username, _tokenFolderPath);
            throw;
        }
    }

    // Helper class for token file serialization/deserialization
    private class TokenFileInfo
    {
        public string Username { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public string AllowedScopes { get; set; } = "*";
        public string AllowedEnvironments { get; set; } = "*";
        public string ExpiresAt { get; set; } = "Never";
        public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        public string? Description { get; set; }
        public string Usage { get; set; } = string.Empty;
    }
}