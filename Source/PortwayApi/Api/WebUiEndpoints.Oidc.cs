namespace PortwayApi.Endpoints;

using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PortwayApi.Auth;
using PortwayApi.Helpers;
using Serilog;

public static partial class WebUiEndpointExtensions
{
    private const string OidcBase = "/ui/api/auth/oidc";

    private static void MapOidcRoutes(WebApplication app, PortwayApi.Services.Configuration.ConfigAuditService configAudit, bool secureCookies)
    {
        void Audit(HttpContext ctx, string action, string target, string? details = null)
            => configAudit.Record(action, "oidc-provider", target, ctx.Connection.RemoteIpAddress?.ToString(), details, null);

        // The sign-in page paints its buttons from this and hides the block when there are none
        app.MapGet("/ui/api/auth/providers", async (AuthDbContext db, IConfiguration config) =>
        {
            if (!OidcEnabled(config)) return Results.Json(new { providers = Array.Empty<object>() });

            var providers = await db.OidcProviders
                .Where(p => p.IsEnabled)
                .OrderBy(p => p.Id)
                .Select(p => new { slug = p.Slug, name = p.Name })
                .ToListAsync();

            return Results.Json(new { providers });
        }).ExcludeFromDescription();

        app.MapGet($"{OidcBase}/{{slug}}/start", async (AuthDbContext db, IConfiguration config, HttpContext ctx, string slug) =>
        {
            var provider = await UsableAsync(db, config, slug);
            if (provider is null) return Results.NotFound();

            try
            {
                var start = await OidcFlow.BeginAsync(provider, RedirectUri(ctx, provider.Slug), ctx.RequestAborted);
                return Results.Redirect(start.AuthorizeUrl);
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException)
            {
                // A provider that is down or misconfigured is an operator problem, not a visitor's
                Log.Error(ex, "Could not read the discovery document for {Provider}", provider.Slug);
                return Results.Redirect(Back(ctx, OidcFlow.Failed));
            }
        }).ExcludeFromDescription();

        // The account is fixed here, from a session that is already authenticated, before the redirect
        // is built. What comes back from the provider is written, never matched.
        app.MapPost("/ui/api/oidc/providers/{slug}/link", async (
            AuthDbContext db, IConfiguration config, HttpContext ctx, AdminUserService users, string slug) =>
        {
            if (ctx.Items[SignedInUserKey] is not int me)
                return Results.Json(new { error = "Sign in to continue" }, statusCode: 401);

            JsonElement body;
            try { body = await JsonSerializer.DeserializeAsync<JsonElement>(ctx.Request.Body); }
            catch (JsonException) { return Results.Json(new { error = "Invalid JSON body" }, statusCode: 400); }

            var provider = await UsableAsync(db, config, slug);
            if (provider is null) return Results.NotFound();

            var clientIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            if (WebUiAuthHelper.CheckAccess(clientIp) is { } blocked)
                return Results.Json(new { error = blocked }, statusCode: 429);

            // A borrowed session must not be able to bolt a second way in onto somebody else's account
            var confirm = body.TryGetProperty("current_password", out var c) ? c.GetString() ?? "" : "";
            if (!await users.ConfirmPasswordAsync(me, confirm))
            {
                WebUiAuthHelper.RecordFailedAttempt(clientIp);
                return Results.Json(new { error = "That password is incorrect", field = "current_password" }, statusCode: 403);
            }
            WebUiAuthHelper.ClearFailedAttempts(clientIp);

            try
            {
                var start = await OidcFlow.BeginAsync(provider, RedirectUri(ctx, provider.Slug), ctx.RequestAborted, linkTo: me);
                return Results.Json(new { authorize_url = start.AuthorizeUrl });
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException)
            {
                Log.Error(ex, "Could not read the discovery document for {Provider}", provider.Slug);
                return Results.Json(new { error = "That provider could not be reached. Check its issuer URL." }, statusCode: 400);
            }
        }).ExcludeFromDescription();

        app.MapDelete("/ui/api/oidc/link", async (HttpContext ctx, AuthDbContext db, AdminUserService users) =>
        {
            if (ctx.Items[SignedInUserKey] is not int me)
                return Results.Json(new { error = "Sign in to continue" }, statusCode: 401);

            var account = await db.AdminUsers.FirstOrDefaultAsync(u => u.Id == me);
            if (account is null) return Results.Json(new { error = "Account not found" }, statusCode: 404);

            // Unbinding an account with no password would leave it with no way to sign in at all
            if (account.PasswordHash.Length == 0)
                return Results.Json(new { error = "Set a password before unlinking, or this account loses its only way in" }, statusCode: 409);

            account.Provider = AdminUserProviders.Local;
            account.ExternalId = null;
            await db.SaveChangesAsync();

            Audit(ctx, "unlink", account.Username);
            Log.Information("Console account {Username} unlinked from its provider", account.Username);

            return Results.Json(new { ok = true });
        }).ExcludeFromDescription();

        app.MapGet($"{OidcBase}/{{slug}}/callback", async (
            AuthDbContext db, IConfiguration config, HttpContext ctx, AdminUserService users, IHttpClientFactory clients,
            string slug, string? code, string? state, string? error) =>
        {
            // The state is spent here whatever happens next, so a code cannot be presented twice
            var flow = OidcFlow.Claim(state);
            if (flow is null) return Results.Redirect(Back(ctx, OidcFlow.Failed));

            if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
                return Results.Redirect(Back(ctx, OidcFlow.Denied));

            var provider = await UsableAsync(db, config, slug);
            if (provider is null || provider.Id != flow.ProviderId)
                return Results.Redirect(Back(ctx, OidcFlow.Failed));

            OidcIdentity? identity;
            try
            {
                identity = await OidcFlow.CompleteAsync(provider, flow, code, clients.CreateClient(), ctx.RequestAborted);
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException)
            {
                Log.Error(ex, "Could not complete the sign-in with {Provider}", provider.Slug);
                identity = null;
            }
            if (identity is null) return Results.Redirect(Back(ctx, OidcFlow.Failed));

            if (flow.LinkTo != 0)
                return await CompleteLinkAsync(db, ctx, provider, flow, identity);

            var (account, problem) = await ResolveAccountAsync(db, provider, identity);
            if (account is null)
            {
                // The provider only, never the subject or the token: a log an operator reads
                // should say which door was tried, not carry the credential that tried it
                Log.Warning("Refused console sign-in through {Provider} ({Problem})", provider.Name, problem);
                return Results.Redirect(Back(ctx, problem));
            }

            account.LastLoginAt = DateTime.UtcNow;
            // A federated account never had a password to change
            account.MustChangePassword = false;
            await db.SaveChangesAsync();

            Log.Information("Console sign-in as {Username} through {Provider}", account.Username, provider.Name);

            IssueSessionCookies(ctx, account.Id, secureCookies);
            return Results.Redirect($"{ctx.Request.PathBase}/ui/dashboard");
        }).ExcludeFromDescription();

        // Provider administration
        app.MapGet("/ui/api/oidc/providers", async (AuthDbContext db) =>
        {
            var providers = await db.OidcProviders.OrderBy(p => p.Id).ToListAsync();
            return Results.Json(new
            {
                providers = providers.Select(p => new
                {
                    id = p.Id,
                    slug = p.Slug,
                    name = p.Name,
                    authority = p.Authority,
                    client_id = p.ClientId,
                    // Never returned, only whether one is set
                    has_client_secret = p.ClientSecret.Length > 0,
                    scopes = p.Scopes,
                    username_claim = p.UsernameClaim,
                    email_claim = p.EmailClaim,
                    is_enabled = p.IsEnabled,
                    create_accounts = p.CreateAccounts,
                    created_role = p.CreatedRole,
                    redirect_uri = RedirectUri(null, p.Slug)
                })
            });
        }).ExcludeFromDescription();

        app.MapPost("/ui/api/oidc/providers", async (HttpContext ctx, AuthDbContext db) =>
        {
            JsonElement body;
            try { body = await JsonSerializer.DeserializeAsync<JsonElement>(ctx.Request.Body); }
            catch (JsonException) { return Results.Json(new { error = "Invalid JSON body" }, statusCode: 400); }

            var provider = new OidcProvider();
            if (Apply(body, provider, isNew: true) is { } problem)
                return Results.Json(new { error = problem.Message, field = problem.Field }, statusCode: 400);

            if (await db.OidcProviders.AnyAsync(p => p.Slug == provider.Slug))
                return Results.Json(new { error = "That key is already in use", field = "slug" }, statusCode: 409);

            db.OidcProviders.Add(provider);
            await db.SaveChangesAsync();

            Audit(ctx, "create", provider.Slug, provider.Authority);
            Log.Information("OIDC provider {Slug} created", provider.Slug);

            return Results.Json(new { ok = true, id = provider.Id });
        }).ExcludeFromDescription();

        app.MapPut("/ui/api/oidc/providers/{id:int}", async (HttpContext ctx, int id, AuthDbContext db) =>
        {
            JsonElement body;
            try { body = await JsonSerializer.DeserializeAsync<JsonElement>(ctx.Request.Body); }
            catch (JsonException) { return Results.Json(new { error = "Invalid JSON body" }, statusCode: 400); }

            var provider = await db.OidcProviders.FirstOrDefaultAsync(p => p.Id == id);
            if (provider is null) return Results.Json(new { error = "Provider not found" }, statusCode: 404);

            if (Apply(body, provider, isNew: false) is { } problem)
                return Results.Json(new { error = problem.Message, field = problem.Field }, statusCode: 400);

            if (await db.OidcProviders.AnyAsync(p => p.Slug == provider.Slug && p.Id != id))
                return Results.Json(new { error = "That key is already in use", field = "slug" }, statusCode: 409);

            await db.SaveChangesAsync();
            // A rotated secret or a moved authority must not be served from the cached document
            OidcFlow.Forget(provider.Id);

            Audit(ctx, "update", provider.Slug, provider.Authority);
            Log.Information("OIDC provider {Slug} updated", provider.Slug);

            return Results.Json(new { ok = true });
        }).ExcludeFromDescription();

        app.MapDelete("/ui/api/oidc/providers/{id:int}", async (HttpContext ctx, int id, AuthDbContext db) =>
        {
            var provider = await db.OidcProviders.FirstOrDefaultAsync(p => p.Id == id);
            if (provider is null) return Results.Json(new { error = "Provider not found" }, statusCode: 404);

            // A binding to a provider that no longer exists is a dangling reference: the account
            // still reports it, and no other provider can adopt the account while it stands
            var bound = await db.AdminUsers.Where(u => u.Provider == provider.Slug).ToListAsync();
            foreach (var account in bound)
            {
                account.Provider = AdminUserProviders.Local;
                account.ExternalId = null;
            }

            // Those without a password have just lost their only way in
            var stranded = bound.Count(u => u.PasswordHash.Length == 0 && u.IsActive);

            db.OidcProviders.Remove(provider);
            await db.SaveChangesAsync();
            OidcFlow.Forget(provider.Id);

            Audit(ctx, "delete", provider.Slug,
                $"{bound.Count} account(s) unbound" + (stranded > 0 ? $", {stranded} now with no way to sign in" : ""));
            Log.Information("OIDC provider {Slug} deleted, {Count} account(s) unbound", provider.Slug, bound.Count);
            if (stranded > 0)
                Log.Warning("{Count} account(s) signed in only through {Slug} and now have no password", stranded, provider.Slug);

            return Results.Json(new { ok = true, unbound_accounts = bound.Count, stranded_accounts = stranded });
        }).ExcludeFromDescription();
    }

    /// <summary>Global kill switch; read per request so flipping it takes effect without a restart</summary>
    private static bool OidcEnabled(IConfiguration config) => config.GetValue("Oidc:Enabled", true);

    private static Task<OidcProvider?> UsableAsync(AuthDbContext db, IConfiguration config, string slug) =>
        OidcEnabled(config)
            ? db.OidcProviders.FirstOrDefaultAsync(p => p.Slug == slug && p.IsEnabled)
            : Task.FromResult<OidcProvider?>(null);

    /// <summary>Back to the sign-in page with a reason the page can show</summary>
    private static string Back(HttpContext ctx, string reason) =>
        $"{ctx.Request.PathBase}/ui/login?sso={reason}";

    /// <summary>
    /// The address registered at the provider. Built from the request when there is one so a
    /// reverse proxy and a PathBase are included, and from configuration when the console asks
    /// what to register.
    /// </summary>
    private static string RedirectUri(HttpContext? ctx, string slug)
    {
        if (ctx is null) return $"{OidcBase}/{slug}/callback";
        return $"{ctx.Request.Scheme}://{ctx.Request.Host}{ctx.Request.PathBase}{OidcBase}/{slug}/callback";
    }

    private sealed record FieldProblem(string Message, string Field);

    private static FieldProblem? Apply(JsonElement body, OidcProvider provider, bool isNew)
    {
        string? Text(string name) => body.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;
        bool? Flag(string name) => body.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean() : null;

        if (Text("slug") is { } slug)
        {
            if (!Regex.IsMatch(slug, @"^[a-z0-9-]{1,32}$"))
                return new FieldProblem("A key may use lowercase letters, numbers and hyphens", "slug");
            provider.Slug = slug;
        }
        else if (isNew) return new FieldProblem("A key is required", "slug");

        if (Text("name") is { } name) provider.Name = name.Trim();
        if (isNew && string.IsNullOrWhiteSpace(provider.Name))
            return new FieldProblem("A name is required", "name");

        if (Text("authority") is { } authority)
        {
            if (!Uri.TryCreate(authority, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                return new FieldProblem("The issuer must be an absolute http or https URL", "authority");
            if (uri.Scheme == "http" && !OidcFlow.AllowsPlainHttp(authority))
                return new FieldProblem("Only a provider on loopback may use plain http", "authority");
            provider.Authority = authority.TrimEnd('/');
        }
        else if (isNew) return new FieldProblem("An issuer URL is required", "authority");

        if (Text("client_id") is { } clientId) provider.ClientId = clientId.Trim();
        if (isNew && string.IsNullOrWhiteSpace(provider.ClientId))
            return new FieldProblem("A client id is required", "client_id");

        // Absent leaves the stored secret alone; empty clears it, which is how a public client is registered
        if (Text("client_secret") is { } secret) provider.ClientSecret = secret;

        if (Text("scopes") is { } scopes && scopes.Trim().Length > 0) provider.Scopes = scopes.Trim();
        if (Text("username_claim") is { } claim && claim.Trim().Length > 0) provider.UsernameClaim = claim.Trim();
        if (Text("email_claim") is { } emailClaim && emailClaim.Trim().Length > 0) provider.EmailClaim = emailClaim.Trim();

        if (Text("created_role") is { } role)
        {
            if (!AdminUserRoles.IsKnown(role)) return new FieldProblem("Unknown role", "created_role");
            provider.CreatedRole = role;
        }

        if (Flag("is_enabled") is { } enabled) provider.IsEnabled = enabled;
        if (Flag("create_accounts") is { } create) provider.CreateAccounts = create;

        return null;
    }

    /// <summary>
    /// Writes the identity onto the account that started the flow. Nothing is matched here: the
    /// account was chosen by an authenticated, password-confirmed session before the redirect.
    /// </summary>
    private static async Task<IResult> CompleteLinkAsync(
        AuthDbContext db, HttpContext ctx, OidcProvider provider, OidcFlow.PendingFlow flow, OidcIdentity identity)
    {
        var account = await db.AdminUsers.FirstOrDefaultAsync(u => u.Id == flow.LinkTo);
        if (account is null || !account.IsActive)
            return Results.Redirect(BackToUsers(ctx, OidcFlow.NotLinked));

        // The same identity must not open two accounts
        var taken = await db.AdminUsers.AnyAsync(u =>
            u.Id != account.Id && u.Provider == provider.Slug && u.ExternalId == identity.Subject);
        if (taken)
        {
            Log.Warning("Refused a link through {Provider}: that identity already belongs to another account", provider.Name);
            return Results.Redirect(BackToUsers(ctx, OidcFlow.NotLinked));
        }

        account.Provider = provider.Slug;
        account.ExternalId = identity.Subject;
        if (identity.Email.Length > 0 && account.Email.Length == 0) account.Email = identity.Email;
        await db.SaveChangesAsync();

        Log.Information("Console account {Username} linked to {Provider}", account.Username, provider.Name);
        return Results.Redirect(BackToUsers(ctx, OidcFlow.Linked));
    }

    private static string BackToUsers(HttpContext ctx, string reason) =>
        $"{ctx.Request.PathBase}/ui/users?link={reason}";

    /// <summary>
    /// Finds the account this identity belongs to. A subject already bound wins; otherwise the
    /// username claim may adopt an existing account, and a new one is created only when the
    /// provider is allowed to. A claim never chooses an account that is already federated elsewhere.
    /// </summary>
    private static async Task<(AdminUser? Account, string Problem)> ResolveAccountAsync(
        AuthDbContext db, OidcProvider provider, OidcIdentity identity)
    {
        var account = await db.AdminUsers.FirstOrDefaultAsync(u =>
            u.Provider == provider.Slug && u.ExternalId == identity.Subject);

        // The claim is matched, never validated: a handle this console would not issue can still
        // name an account, and one that matches nothing falls through to the address below
        if (account is null && identity.Username.Length > 0)
            account = await Linkable(db, provider, u => u.Username == identity.Username);

        // Only when the provider says it verified the address. An unverified email claim is a
        // claim to somebody else's account.
        if (account is null && identity.EmailVerified && identity.Email.Length > 0)
            account = await Linkable(db, provider, u => u.Email == identity.Email);

        if (account is not null && string.IsNullOrEmpty(account.ExternalId))
        {
            account.Provider = provider.Slug;
            account.ExternalId = identity.Subject;
        }

        if (account is null)
        {
            if (!provider.CreateAccounts)
            {
                // The subject is the only handle the link command takes, and a refused sign-in is
                // the one place it is ever seen
                Log.Warning(
                    "{Provider} sign-in failed: subject {Subject} ({PresentedAs}) is not linked to an account. " +
                    "Link it from the Users page, or turn on account creation for this provider.",
                    provider.Name, identity.Subject,
                    identity.Username.Length > 0 ? identity.Username : "no name claim");

                if (UnusableNameNote(provider, identity) is { Length: > 0 } nameNote)
                    Log.Warning("{Note}", nameNote);
                Log.Warning("{Note}", WhyNoEmailMatch(provider, identity));
                return (null, OidcFlow.NoAccount);
            }

            account = Provision(db, provider, identity);
        }

        return account.IsActive ? (account, "") : (null, OidcFlow.Inactive);
    }

    /// <summary>An account already tied to another provider is not a candidate: linking it would move it</summary>
    private static Task<AdminUser?> Linkable(
        AuthDbContext db, OidcProvider provider, System.Linq.Expressions.Expression<Func<AdminUser, bool>> match) =>
        db.AdminUsers
            .Where(u => u.Provider == AdminUserProviders.Local || u.Provider == provider.Slug)
            .Where(match)
            .FirstOrDefaultAsync();

    /// <summary>Says so when the claim arrived but this console would never issue that handle</summary>
    private static string UnusableNameNote(OidcProvider provider, OidcIdentity identity)
    {
        if (identity.Username.Length == 0) return "";
        if (AdminUserService.ValidateUsername(identity.Username) is null) return "";

        return $"{provider.Name} {provider.UsernameClaim} claim \"{identity.Username}\" is not a handle this console issues, " +
            "so it only matches an existing account by that exact name. Set the provider's username claim to a plain " +
            "handle, or give the account an email address and let the provider send a verified one.";
    }

    /// <summary>Four different reasons an address did not match, and a refusal naming none of them audits the wrong thing</summary>
    private static string WhyNoEmailMatch(OidcProvider provider, OidcIdentity identity)
    {
        if (identity.Email.Length == 0)
            return $"No address to match on: {provider.Name} sent nothing in its {provider.EmailClaim} claim. " +
                   $"The token carried: {string.Join(", ", identity.ClaimNames.OrderBy(n => n))}";

        if (!identity.EmailVerified)
            return $"{provider.Name} sent {identity.Email} but did not mark it verified, so it was not matched. " +
                   "An unverified address is a claim to somebody else's account.";

        return $"No active account carries the address {identity.Email}, and none is bound to this provider's subject.";
    }

    private static AdminUser Provision(AuthDbContext db, OidcProvider provider, OidcIdentity identity)
    {
        // A handle this console would not issue is replaced rather than refused
        var username = identity.Username;
        if (AdminUserService.ValidateUsername(username) is not null)
            username = DeriveUsername(identity.Email.Length > 0 ? identity.Email : identity.Subject);

        var created = new AdminUser
        {
            Username = username,
            // No password: this account signs in through the provider only
            PasswordHash = string.Empty,
            Provider = provider.Slug,
            ExternalId = identity.Subject,
            Email = identity.EmailVerified ? identity.Email : string.Empty,
            Role = provider.CreatedRole,
        };
        db.AdminUsers.Add(created);
        return created;
    }

    /// <summary>A handle out of an address or a subject, keeping only characters a username may hold</summary>
    private static string DeriveUsername(string source)
    {
        var at = source.IndexOf('@');
        var head = at > 0 ? source[..at] : source;
        var cleaned = Regex.Replace(head, @"[^a-zA-Z0-9._-]", "-").Trim('-');
        if (cleaned.Length == 0) cleaned = "user";
        return cleaned.Length > AdminUserService.UsernameMax ? cleaned[..AdminUserService.UsernameMax] : cleaned;
    }

}
