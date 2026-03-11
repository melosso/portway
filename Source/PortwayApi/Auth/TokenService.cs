namespace PortwayApi.Auth;

using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Serilog;

public class TokenService
{
    private readonly AuthDbContext _dbContext;
    private readonly string _tokenFolderPath;
    
    public TokenService(AuthDbContext dbContext)
    {
        _dbContext = dbContext;
        _tokenFolderPath = Path.Combine(Directory.GetCurrentDirectory(), "tokens");
        
        // Ensure tokens directory exists
        if (!Directory.Exists(_tokenFolderPath))
        {
            Directory.CreateDirectory(_tokenFolderPath);
            Log.Debug("Created tokens directory: {Directory}", _tokenFolderPath);
        }
    }

    /// <summary>
    /// Get the first token ever created (lowest ID) for Free mode restrictions
    /// </summary>
    public async Task<AuthToken?> GetFirstTokenAsync()
    {
        return await _dbContext.Tokens
            .Where(t => t.RevokedAt == null && (t.ExpiresAt == null || t.ExpiresAt > DateTime.UtcNow))
            .OrderBy(t => t.Id)
            .FirstOrDefaultAsync();
    }
    
    /// <summary>
    /// Generate a new token for a user with optional scopes and expiration
    /// </summary>
    public async Task<string> GenerateTokenAsync(
        string username, 
        string allowedScopes = "*",
        string allowedEnvironments = "*",
        string description = "", 
        int? expiresInDays = null)
    {
        // Generate a random token
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        string token = RandomNumberGenerator.GetString(chars, 128);
        
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
        
        // Add to database
        _dbContext.Tokens.Add(tokenEntry);
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
        
        Log.Information("Created new token (ID: {TokenId}) for user: {Username}", tokenEntry.Id, username);
        
        return token;
    }
    
    /// <summary>
    /// Verify if a token is valid (not revoked or expired)
    /// </summary>
    public async Task<bool> VerifyTokenAsync(string token)
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
    
    /// <summary>
    /// Verify if a token is valid for a specific username
    /// </summary>
    public async Task<bool> VerifyTokenAsync(string token, string username)
    {
        // Get active tokens for this user
        var tokens = await _dbContext.Tokens
            .Where(t => t.Username == username && 
                   t.RevokedAt == null && 
                   (t.ExpiresAt == null || t.ExpiresAt > DateTime.UtcNow))
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
    
    /// <summary>
    /// Verify if a token has access to a specific endpoint
    /// </summary>
    public async Task<bool> VerifyTokenForEndpointAsync(string token, string endpointName)
    {
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(endpointName))
            return false;
            
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
                // Check if token has access to endpoint
                return storedToken.HasAccessToEndpoint(endpointName);
            }
        }
        
        return false;
    }

    public async Task<bool> VerifyTokenForEnvironmentAsync(string token, string environment)
    {
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(environment))
            return false;
            
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
                // Check if token has access to environment
                return storedToken.HasAccessToEnvironment(environment);
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// Get token details by token string (for middleware use)
    /// </summary>
    public async Task<AuthToken?> GetTokenDetailsByTokenAsync(string token)
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
                // Log successful token access (debug level to avoid spam)
                Log.Debug("Token access: {Username} (ID: {TokenId})", storedToken.Username, storedToken.Id);
                return storedToken;
            }
        }
        
        // Log failed token access attempt
        Log.Warning("Failed token access attempt with token: {TokenPrefix}...", 
            token.Length > 10 ? token[..10] : token);
        
        return null;
    }
    
    /// <summary>
    /// Helper method to hash a token using PBKDF2 with SHA256
    /// </summary>
    private string HashToken(string token, byte[] salt)
    {
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(token, salt, 10000, HashAlgorithmName.SHA256, 32);
        return Convert.ToBase64String(hash);
    }
    
    /// <summary>
    /// Helper method to generate a random salt
    /// </summary>
    private byte[] GenerateSalt()
    {
        byte[] salt = new byte[16];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(salt);
        }
        return salt;
    }
    
    /// <summary>
    /// Helper method to save a token to a file with enhanced details
    /// </summary>
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
            string filePath = Path.Combine(_tokenFolderPath, $"{username}.txt");
            
            // Create a more informative token file with usage instructions
            var tokenInfo = new
            {
                Username = username,
                Token = token,
                AllowedScopes = allowedScopes,
                AllowedEnvironments = allowedEnvironments,
                ExpiresAt = expiresAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Never",
                CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                Usage = "Use this token in the Authorization header as: Bearer <token>"
            };
            
            var options = new JsonSerializerOptions { WriteIndented = true };
            string tokenJson = JsonSerializer.Serialize(tokenInfo, options);
            await File.WriteAllTextAsync(filePath, tokenJson);
            
            Log.Debug("Token file saved to {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save token file for user {Username}", username);
            throw;
        }
    }
    
    /// <summary>
    /// Get all active tokens (not revoked and not expired)
    /// </summary>
    public async Task<IEnumerable<AuthToken>> GetActiveTokensAsync()
    {
        return await _dbContext.Tokens
            .Where(t => t.RevokedAt == null && (t.ExpiresAt == null || t.ExpiresAt > DateTime.UtcNow))
            .OrderBy(t => t.Id) // Order by ID to show first token first
            .ToListAsync();
    }
    
    /// <summary>
    /// Get all tokens (including expired and revoked)
    /// </summary>
    public async Task<IEnumerable<AuthToken>> GetAllTokensAsync()
    {
        return await _dbContext.Tokens
            .OrderBy(t => t.Id) // Order by ID to show first token first
            .ToListAsync();
    }
    
    /// <summary>
    /// Returns null if the token can be revoked, or an error message if it is protected.
    /// A token is protected when it is the last active token, or the last active token
    /// that holds full wildcard (*/*)  permissions.
    /// </summary>
    public async Task<string?> GetRevokeBlockReasonAsync(int tokenId)
    {
        var active = await _dbContext.Tokens
            .Where(t => t.RevokedAt == null && (t.ExpiresAt == null || t.ExpiresAt > DateTime.UtcNow))
            .ToListAsync();

        var target = active.FirstOrDefault(t => t.Id == tokenId);
        if (target == null) return null; // not found or already archived

        if (active.Count <= 1)
            return "Cannot revoke token";

        var fullPerm = active.Where(t => t.AllowedScopes == "*" && t.AllowedEnvironments == "*").ToList();
        if (fullPerm.Count == 1 && fullPerm[0].Id == tokenId)
            return "Cannot delete token";

        return null;
    }

    /// <summary>
    /// Revoke (soft-delete / archive) a token by ID.
    /// Returns false when the token is not found or is protected by the last-token guard.
    /// </summary>
    public async Task<bool> RevokeTokenAsync(int tokenId)
    {
        // Enforce last-token guard even when called directly (e.g. from CLI)
        var blockReason = await GetRevokeBlockReasonAsync(tokenId);
        if (blockReason != null)
        {
            Log.Warning("Refused to archive token {TokenId}: {Reason}", tokenId, blockReason);
            return false;
        }

        var token = await _dbContext.Tokens.FindAsync(tokenId);
        if (token == null) return false;

        token.RevokedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
        
        // Also append a .revoked suffix to the token file
        try
        {
            string tokenFilePath = Path.Combine(_tokenFolderPath, $"{token.Username}.txt");
            if (File.Exists(tokenFilePath))
            {
                string revokedPath = Path.Combine(_tokenFolderPath, $"{token.Username}.revoked.txt");
                if (File.Exists(revokedPath))
                    File.Delete(revokedPath);
                    
                File.Move(tokenFilePath, revokedPath);
                Log.Information("Marked token file as revoked: {FilePath}", revokedPath);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not rename token file for revoked token");
        }
        
        await LogAuditAsync(token.Id, token.Username, "Revoked", token.TokenHash, null,
            JsonSerializer.Serialize(new { RevokedAt = token.RevokedAt?.ToString("yyyy-MM-dd HH:mm:ss") }));

        Log.Information("Revoked token ID: {TokenId} for user: {Username}", tokenId, token.Username);
        return true;
    }
    
    /// <summary>
    /// Unarchive (restore) a previously revoked token by ID.
    /// Returns false when the token is not found or is not revoked.
    /// </summary>
    public async Task<bool> UnarchiveTokenAsync(int tokenId)
    {
        var token = await _dbContext.Tokens.FindAsync(tokenId);
        if (token == null || token.RevokedAt == null) return false;

        token.RevokedAt = null;
        await _dbContext.SaveChangesAsync();

        // Rename .revoked.txt back to .txt
        try
        {
            string revokedPath = Path.Combine(_tokenFolderPath, $"{token.Username}.revoked.txt");
            if (File.Exists(revokedPath))
            {
                string tokenFilePath = Path.Combine(_tokenFolderPath, $"{token.Username}.txt");
                if (File.Exists(tokenFilePath))
                    File.Delete(tokenFilePath);
                File.Move(revokedPath, tokenFilePath);
                Log.Information("Restored token file: {FilePath}", tokenFilePath);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not restore token file for unarchived token");
        }

        await LogAuditAsync(token.Id, token.Username, "Unarchived", null, null,
            JsonSerializer.Serialize(new { RestoredAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") }));

        Log.Information("Unarchived token ID: {TokenId} for user: {Username}", tokenId, token.Username);
        return true;
    }

    /// <summary>
    /// Set token expiration by ID
    /// </summary>
    public async Task<bool> SetTokenExpirationAsync(int tokenId, DateTime expirationDate)
    {
        var token = await _dbContext.Tokens.FindAsync(tokenId);
        if (token == null) return false;
        
        token.ExpiresAt = expirationDate;
        await _dbContext.SaveChangesAsync();
        
        return true;
    }
    
    /// <summary>
    /// Update token allowed environments by ID
    /// </summary>
    public async Task<bool> UpdateTokenEnvironmentsAsync(int tokenId, string environments)
    {
        var token = await _dbContext.Tokens.FindAsync(tokenId);
        if (token == null) return false;
        string old = token.AllowedEnvironments;
        token.AllowedEnvironments = environments;
        await _dbContext.SaveChangesAsync();
        await LogAuditAsync(token.Id, token.Username, "EnvironmentsUpdated", null, null,
            JsonSerializer.Serialize(new { OldEnvironments = old, NewEnvironments = environments,
                UpdatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") }));
        return true;
    }

    /// <summary>
    /// Update token scopes by ID
    /// </summary>
    public async Task<bool> UpdateTokenScopesAsync(int tokenId, string scopes)
    {
        var token = await _dbContext.Tokens.FindAsync(tokenId);
        if (token == null) return false;
        
        string oldScopes = token.AllowedScopes;
        token.AllowedScopes = scopes;
        await _dbContext.SaveChangesAsync();
        
        // Log the token scope update in audit trail
        await LogAuditAsync(token.Id, token.Username, "ScopesUpdated", null, null,
            JsonSerializer.Serialize(new 
            { 
                OldScopes = oldScopes,
                NewScopes = scopes,
                UpdatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
            }));
        
        return true;
    }
    
    /// <summary>
    /// Update token description by ID
    /// </summary>
    public async Task<bool> UpdateTokenDescriptionAsync(int tokenId, string description)
    {
        var token = await _dbContext.Tokens.FindAsync(tokenId);
        if (token == null) return false;
        string old = token.Description ?? "";
        token.Description = description;
        await _dbContext.SaveChangesAsync();
        await LogAuditAsync(token.Id, token.Username, "DescriptionUpdated", null, null,
            JsonSerializer.Serialize(new { OldDescription = old, NewDescription = description,
                UpdatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") }));
        return true;
    }

    /// <summary>
    /// Log audit information for token operations
    /// </summary>
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
                Source = "PortwayApi",
                IpAddress = null, // Could be enhanced to capture actual IP from HttpContext
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
    
    /// <summary>
    /// Get audit log entries for a specific token or user
    /// </summary>
    public async Task<List<AuthTokenAudit>> GetAuditLogAsync(string? username = null, int? tokenId = null, int maxRecords = 100)
    {
        var query = _dbContext.TokenAudits.AsQueryable();
        
        if (!string.IsNullOrEmpty(username))
        {
            query = query.Where(a => a.Username == username);
        }
        
        if (tokenId.HasValue)
        {
            query = query.Where(a => a.TokenId == tokenId);
        }
        
        return await query
            .OrderByDescending(a => a.Timestamp)
            .Take(maxRecords)
            .ToListAsync();
    }
    
    /// <summary>
    /// Check for recent token operations (useful for detecting rotations)
    /// </summary>
    public async Task<bool> HasRecentTokenActivity(string username, TimeSpan timeSpan)
    {
        var cutoffTime = DateTime.UtcNow - timeSpan;
        
        return await _dbContext.TokenAudits
            .AnyAsync(a => a.Username == username && 
                          a.Timestamp > cutoffTime && 
                          (a.Operation == "Rotated" || a.Operation == "Created" || a.Operation == "Revoked"));
    }
    
    /// <summary>
    /// Get the most recent audit entry for a username (useful for detecting recent changes)
    /// </summary>
    public async Task<AuthTokenAudit?> GetLatestAuditEntryAsync(string username)
    {
        return await _dbContext.TokenAudits
            .Where(a => a.Username == username)
            .OrderByDescending(a => a.Timestamp)
            .FirstOrDefaultAsync();
    }
}