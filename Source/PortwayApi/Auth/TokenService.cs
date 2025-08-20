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
            Log.Debug("✅ Created tokens directory: {Directory}", _tokenFolderPath);
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
        // Generate a random token with improved strength
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
        
        // Save token to file
        await SaveTokenToFileAsync(username, token, allowedScopes, allowedEnvironments, expiresAt, description);
        
        Log.Information("🔑 Created new token (ID: {TokenId}) for user: {Username}", tokenEntry.Id, username);
        
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
                return storedToken;
            }
        }
        
        return null;
    }
    
    /// <summary>
    /// Helper method to hash a token using PBKDF2 with SHA256
    /// </summary>
    private string HashToken(string token, byte[] salt)
    {
        using (var pbkdf2 = new Rfc2898DeriveBytes(token, salt, 10000, HashAlgorithmName.SHA256))
        {
            byte[] hash = pbkdf2.GetBytes(32); // 256 bits
            return Convert.ToBase64String(hash);
        }
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
            
            Log.Debug("🔑 Token file saved to {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ Failed to save token file for user {Username}", username);
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
    /// Revoke a token by ID
    /// </summary>
    public async Task<bool> RevokeTokenAsync(int tokenId)
    {
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
                Log.Information("✅ Marked token file as revoked: {FilePath}", revokedPath);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "⚠️ Could not rename token file for revoked token");
        }
        
        Log.Information("🗑️ Revoked token ID: {TokenId} for user: {Username}", tokenId, token.Username);
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
    /// Update token scopes by ID
    /// </summary>
    public async Task<bool> UpdateTokenScopesAsync(int tokenId, string scopes)
    {
        var token = await _dbContext.Tokens.FindAsync(tokenId);
        if (token == null) return false;
        
        token.AllowedScopes = scopes;
        await _dbContext.SaveChangesAsync();
        
        return true;
    }
}