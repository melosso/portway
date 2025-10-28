namespace PortwayApi.Auth;

/// <summary>
/// Represents an audit log entry for token operations
/// </summary>
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
    public string Source { get; set; } = "PortwayApi"; // Source application
    public string? IpAddress { get; set; } = null; // For future use
    public string? UserAgent { get; set; } = null; // For future use
}