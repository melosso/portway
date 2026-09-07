namespace PortwayApi.Auth;

/// <summary>A registered OpenID Connect provider: Authelia, Authentik, Pocket ID, Keycloak, or anything else publishing a discovery document</summary>
public class OidcProvider
{
    public int Id { get; set; }

    /// <summary>Url-safe key in the callback path, so the redirect URI registered at the provider survives renaming the label</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Button label on the sign-in page</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Issuer URL; discovery is read from {Authority}/.well-known/openid-configuration</summary>
    public string Authority { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    /// <summary>Write-only: never returned by the API, only whether one is set</summary>
    public string ClientSecret { get; set; } = string.Empty;

    public string Scopes { get; set; } = "openid profile email";

    /// <summary>Authelia, Authentik and Pocket ID all send preferred_username</summary>
    public string UsernameClaim { get; set; } = "preferred_username";

    /// <summary>Pocket ID and Authelia both send email; used to match an account by address</summary>
    public string EmailClaim { get; set; } = "email";

    public bool IsEnabled { get; set; }

    /// <summary>Off means an unknown subject is refused rather than given an account</summary>
    public bool CreateAccounts { get; set; }

    /// <summary>Role given to an account created on first sign-in</summary>
    public string CreatedRole { get; set; } = AdminUserRoles.Viewer;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
