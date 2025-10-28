namespace TokenGenerator.Classes;

/// <summary>
/// Represents management configuration including passphrase protection for auth.db
/// </summary>
public class Management
{
    /// <summary>
    /// Primary key
    /// </summary>
    public int Id { get; set; }
    
    /// <summary>
    /// Hashed passphrase for protecting the management tool
    /// </summary>
    public string PassphraseHash { get; set; } = string.Empty;
    
    /// <summary>
    /// Salt used for passphrase hashing
    /// </summary>
    public string PassphraseSalt { get; set; } = string.Empty;
    
    /// <summary>
    /// When the passphrase was created
    /// </summary>
    public DateTime CreatedAt { get; set; }
    
    /// <summary>
    /// When the passphrase was last updated
    /// </summary>
    public DateTime? UpdatedAt { get; set; }
    
    /// <summary>
    /// Number of failed authentication attempts
    /// </summary>
    public int FailedAttempts { get; set; }
    
    /// <summary>
    /// When the last failed attempt occurred
    /// </summary>
    public DateTime? LastFailedAttempt { get; set; }
    
    /// <summary>
    /// When the account is locked until (if locked)
    /// </summary>
    public DateTime? LockedUntil { get; set; }
    
    /// <summary>
    /// Additional configuration settings in JSON format
    /// </summary>
    public string Settings { get; set; } = "{}";
}