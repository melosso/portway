using System;

namespace TokenGenerator.Classes;

public class AuthToken
{
    public int Id { get; set; }
    public required string Username { get; set; } = $"user_{Guid.NewGuid().ToString("N")[..8]}";
    public required string TokenHash { get; set; } = string.Empty;
    public required string TokenSalt { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; } = null;        
    public DateTime? ExpiresAt { get; set; } = null;
    public string AllowedScopes { get; set; } = "*"; // Default to full access
    public string AllowedEnvironments { get; set; } = "*"; // Default to full access
    public string Description { get; set; } = string.Empty;
    public bool IsActive => RevokedAt == null && (ExpiresAt == null || ExpiresAt > DateTime.UtcNow);
}

public class AuthTokenAudit
{
    public int Id { get; set; }
    public int? TokenId { get; set; } // Nullable in case token is deleted
    public required string Username { get; set; } = string.Empty;
    public required string Operation { get; set; } = string.Empty; // Created, Revoked, Rotated, Updated, etc.
    public string? OldTokenHash { get; set; } = null; // For rotation tracking
    public string? NewTokenHash { get; set; } = null; // For rotation tracking
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Details { get; set; } = string.Empty; // JSON metadata
    public string Source { get; set; } = "TokenGenerator"; // Source application
    public string? IpAddress { get; set; } = null; // For future use
    public string? UserAgent { get; set; } = null; // For future use
}

public class CommandLineOptions
{
    public string? DatabasePath { get; set; }
    public string? TokensFolder { get; set; }
    public string? Username { get; set; }
    public string? Scopes { get; set; }
    public string? Environments { get; set; }
    public string? Description { get; set; }
    public int? ExpiresInDays { get; set; }
    public bool ShowHelp { get; set; }
    public bool Verbose { get; set; }
    public bool NoAuth { get; set; } // Disable passphrase protection for Docker/container environments
}

public class AppConfig
{
    public string DatabasePath { get; set; } = "auth.db";
    public string TokensFolder { get; set; } = "tokens";
    public bool EnableDetailedLogging { get; set; } = false;
}