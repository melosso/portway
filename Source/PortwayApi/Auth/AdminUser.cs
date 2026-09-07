namespace PortwayApi.Auth;

/// <summary>A console account. Local accounts have a password hash; federated ones have a provider and subject instead.</summary>
public class AdminUser
{
    public int Id { get; set; }

    public string Username { get; set; } = string.Empty;

    /// <summary>PBKDF2 hash for local accounts; empty for federated ones</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>"local" today; an OIDC provider slug once federation lands</summary>
    public string Provider { get; set; } = AdminUserProviders.Local;

    /// <summary>The provider's subject claim, so a renamed federated account still resolves</summary>
    public string? ExternalId { get; set; }

    /// <summary>Optional. When set, a provider identity carrying this address binds to this account.</summary>
    public string Email { get; set; } = string.Empty;

    public string Role { get; set; } = AdminUserRoles.Administrator;

    public bool IsActive { get; set; } = true;

    /// <summary>Set on a password that was generated or came from configuration; cleared once the account picks its own</summary>
    public bool MustChangePassword { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastLoginAt { get; set; }
}

public static class AdminUserRoles
{
    public const string Administrator = "administrator";
    public const string Viewer = "viewer";

    public static bool IsKnown(string role) =>
        role is Administrator or Viewer;
}

public static class AdminUserProviders
{
    public const string Local = "local";
}
