namespace PortwayApi.Auth;

using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Serilog;

public sealed record OidcIdentity(string Subject, string Username, string Email, bool EmailVerified, IReadOnlyCollection<string> ClaimNames);

/// <summary>
/// Authorization code with PKCE against any provider publishing a discovery document.
/// ConfigurationManager caches the document and its signing keys; JsonWebTokenHandler validates
/// the id_token against them. What is written here is the redirect, the exchange and the one-time state.
/// </summary>
public static class OidcFlow
{
    public const string Failed = "failed";
    public const string Denied = "denied";
    public const string NoAccount = "no_account";
    public const string Inactive = "inactive";
    public const string Linked = "linked";
    public const string NotLinked = "not_linked";

    /// <summary>As long as a person takes to sign in at the provider</summary>
    public static readonly TimeSpan FlowLifetime = TimeSpan.FromMinutes(10);

    // Per process. Portway is one instance over one SQLite file, so a sign-in that starts here finishes here.
    // Behind more than one instance this needs a shared store, or sticky sessions.
    private static readonly ConcurrentDictionary<string, PendingFlow> Pending = new(StringComparer.Ordinal);

    // Keyed on the authority too, so editing a provider's URL drops the document cached for the old one
    private static readonly ConcurrentDictionary<string, ConfigurationManager<OpenIdConnectConfiguration>> Documents = new(StringComparer.Ordinal);

    /// <summary>
    /// LinkTo is zero for a sign-in. When it names an account, the flow is that account binding a
    /// provider identity to itself: the callback writes what comes back instead of matching on it.
    /// The account was chosen by an authenticated session before the redirect, never by a claim.
    /// </summary>
    public sealed record PendingFlow(int ProviderId, string Verifier, string Nonce, string RedirectUri, DateTime ExpiresAt, int LinkTo = 0);

    public sealed record Start(string AuthorizeUrl, string State);

    public static string MetadataAddress(string authority) =>
        $"{authority.TrimEnd('/')}/.well-known/openid-configuration";

    /// <summary>A self-hosted provider on loopback may use plain http; anything else must be https or the tokens travel in the clear</summary>
    public static bool AllowsPlainHttp(string authority) =>
        Uri.TryCreate(authority, UriKind.Absolute, out var uri) && uri.IsLoopback;

    public static Task<OpenIdConnectConfiguration> DocumentAsync(OidcProvider provider, CancellationToken token) =>
        Documents.GetOrAdd($"{provider.Id}|{provider.Authority}", _ => new ConfigurationManager<OpenIdConnectConfiguration>(
            MetadataAddress(provider.Authority),
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever { RequireHttps = !AllowsPlainHttp(provider.Authority) })).GetConfigurationAsync(token);

    /// <summary>Dropped when a provider is edited or removed, so a rotated secret is not served from cache</summary>
    public static void Forget(int providerId)
    {
        foreach (var key in Documents.Keys)
            if (key.StartsWith(providerId + "|", StringComparison.Ordinal))
                Documents.TryRemove(key, out _);
    }

    public static async Task<Start> BeginAsync(OidcProvider provider, string redirectUri, CancellationToken token, int linkTo = 0) =>
        Begin(await DocumentAsync(provider, token), provider, redirectUri, linkTo);

    /// <summary>Split from the fetch so the redirect and its one-time state are testable without a provider</summary>
    internal static Start Begin(OpenIdConnectConfiguration document, OidcProvider provider, string redirectUri, int linkTo = 0)
    {
        if (string.IsNullOrEmpty(document.AuthorizationEndpoint))
            throw new InvalidOperationException("The provider's discovery document declares no authorization endpoint.");

        var state = Token(32);
        var nonce = Token(32);
        var verifier = Token(64);
        var challenge = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        Pending[state] = new PendingFlow(provider.Id, verifier, nonce, redirectUri, DateTime.UtcNow.Add(FlowLifetime), linkTo);

        var query = new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = provider.ClientId,
            ["redirect_uri"] = redirectUri,
            ["scope"] = string.IsNullOrWhiteSpace(provider.Scopes) ? "openid profile email" : provider.Scopes.Trim(),
            ["state"] = state,
            ["nonce"] = nonce,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256"
        };

        return new Start(Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(document.AuthorizationEndpoint, query), state);
    }

    /// <summary>A state is spent by the first callback presenting it, so a replayed code buys no second attempt</summary>
    public static PendingFlow? Claim(string? state)
    {
        if (string.IsNullOrEmpty(state) || !Pending.TryRemove(state, out var flow)) return null;
        return flow.ExpiresAt <= DateTime.UtcNow ? null : flow;
    }

    public static async Task<OidcIdentity?> CompleteAsync(OidcProvider provider, PendingFlow flow, string code, HttpClient http, CancellationToken token)
    {
        var document = await DocumentAsync(provider, token);
        if (string.IsNullOrEmpty(document.TokenEndpoint)) return null;

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = flow.RedirectUri,
            ["client_id"] = provider.ClientId,
            ["code_verifier"] = flow.Verifier
        };

        // PKCE alone authenticates a public client, which is how Pocket ID can register one.
        // A confidential client authenticates the way its provider says it will: most default to
        // HTTP Basic, and answer invalid_client when the secret arrives in the form body instead.
        var basic = !string.IsNullOrEmpty(provider.ClientSecret) && PrefersBasic(document);
        if (!string.IsNullOrEmpty(provider.ClientSecret) && !basic) form["client_secret"] = provider.ClientSecret;

        using var request = new HttpRequestMessage(HttpMethod.Post, document.TokenEndpoint) { Content = new FormUrlEncodedContent(form) };
        if (basic)
        {
            // The spec requires both halves form-urlencoded before they are joined and encoded
            var credentials = $"{Uri.EscapeDataString(provider.ClientId)}:{Uri.EscapeDataString(provider.ClientSecret)}";
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials)));
        }

        using var response = await http.SendAsync(request, token);
        var payload = await response.Content.ReadAsStringAsync(token);

        if (!response.IsSuccessStatusCode)
        {
            // The body holds the provider's own error code, the only thing separating a misconfigured client from a stale code
            var how = provider.ClientSecret.Length == 0 ? "no secret (public client)"
                    : basic ? "client_secret_basic" : "client_secret_post";
            Log.Warning("Token exchange with {Provider} failed with {Status} using {Method}: {Body}",
                provider.Slug, (int)response.StatusCode, how, Truncate(payload));
            return null;
        }

        var idToken = (JsonNode.Parse(payload) as JsonObject)?["id_token"]?.GetValue<string>();
        if (string.IsNullOrEmpty(idToken))
        {
            Log.Warning("Provider {Provider} returned no id_token. Check that the openid scope is granted.", provider.Slug);
            return null;
        }

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(idToken, new TokenValidationParameters
        {
            ValidIssuer = document.Issuer,
            ValidAudience = provider.ClientId,
            IssuerSigningKeys = document.SigningKeys,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true
        });

        if (!result.IsValid)
        {
            Log.Warning(result.Exception, "The id_token from {Provider} did not validate.", provider.Slug);
            return null;
        }

        // Binds the token to the redirect this process started; without it a token minted for another session would pass
        if (Text(result.Claims, "nonce") != flow.Nonce)
        {
            Log.Warning("The id_token from {Provider} carried the wrong nonce.", provider.Slug);
            return null;
        }

        var subject = Text(result.Claims, "sub");
        if (string.IsNullOrEmpty(subject)) return null;

        var verified = result.Claims.TryGetValue("email_verified", out var v) && v switch
        {
            bool flag => flag,
            string text => text.Equals("true", StringComparison.OrdinalIgnoreCase),
            _ => false
        };

        return new OidcIdentity(
            subject,
            Text(result.Claims, provider.UsernameClaim),
            Text(result.Claims, provider.EmailClaim),
            verified,
            result.Claims.Keys.ToArray());
    }

    /// <summary>
    /// Which client authentication the provider asked for. When it advertises the methods it
    /// supports, basic wins unless only post is offered; when it advertises nothing, the spec's
    /// default is basic.
    /// </summary>
    private static bool PrefersBasic(OpenIdConnectConfiguration document)
    {
        var methods = document.TokenEndpointAuthMethodsSupported;
        if (methods is null || methods.Count == 0) return true;
        if (methods.Contains("client_secret_basic")) return true;
        if (methods.Contains("client_secret_post")) return false;
        return true;
    }

    public static int Prune(DateTime now)
    {
        var removed = 0;
        foreach (var (key, flow) in Pending)
            if (flow.ExpiresAt <= now && Pending.TryRemove(key, out _)) removed++;
        return removed;
    }

    private static string Token(int bytes) =>
        Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(bytes));

    private static string Text(IDictionary<string, object> claims, string name) =>
        claims.TryGetValue(name, out var value) ? value as string ?? value.ToString() ?? "" : "";

    private static string Truncate(string body) => body.Length <= 400 ? body : body[..400];

    internal static void Reset()
    {
        Pending.Clear();
        Documents.Clear();
    }
}
