namespace PortwayApi.Endpoints;

using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using PortwayApi.Auth;
using PortwayApi.Helpers;
using Serilog;

public static partial class WebUiEndpointExtensions
{
    private static void MapUserRoutes(WebApplication app, PortwayApi.Services.Configuration.ConfigAuditService configAudit)
    {
        void Audit(HttpContext ctx, string action, string target, string? details = null)
            => configAudit.Record(action, "user", target, ctx.Connection.RemoteIpAddress?.ToString(), details, null);

        // Changing who can sign in asks for the caller's own password, not just their session
        async Task<IResult?> RefuseUnconfirmed(HttpContext ctx, AdminUserService users, JsonElement body)
        {
            if (ctx.Items[SignedInUserKey] is not int me)
                return PortwayResults.MinimalError("Sign in to continue", 401);

            var clientIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            if (WebUiAuthHelper.BeginAttempt(clientIp, $"#{me}") is { } blocked)
                return PortwayResults.MinimalError(blocked, 429);

            var confirm = body.TryGetProperty("current_password", out var c) ? c.GetString() ?? "" : "";
            if (await users.ConfirmPasswordAsync(me, confirm))
            {
                WebUiAuthHelper.ClearFailedAttempts(clientIp, $"#{me}");
                return null;
            }

            Log.Warning("Account change refused: the password for the signed-in account was wrong, from {ClientIp}", clientIp);
            return PortwayResults.MinimalError("That password is incorrect", 403, "current_password");
        }

        app.MapGet("/_/api/avatars/candidates", (HttpRequest request, PortwayApi.Services.Avatars.AvatarService avatars) =>
        {
            var count = int.TryParse(request.Query["count"], out var c) ? Math.Clamp(c, 1, 24) : 12;
            var candidates = Enumerable.Range(0, count)
                .Select(_ => Services.Avatars.AvatarService.Random())
                .Select(a => new { style = a.Style, seed = a.Seed, avatar = avatars.Render(a.Style, a.Seed) })
                .Where(a => a.avatar is not null);
            return Results.Json(new { candidates });
        }).ExcludeFromDescription();

        app.MapGet("/_/api/users", async (
            HttpContext ctx,
            AdminUserService users,
            PortwayApi.Services.Avatars.AvatarService avatars) =>
        {
            var all = await users.ListAsync();
            return Results.Json(new
            {
                signed_in_user_id = ctx.Items[SignedInUserKey] as int?,
                users = all.Select(u => new
                {
                    id = u.Id,
                    username = u.Username,
                    email = u.Email,
                    display_name = u.DisplayName,
                    role = u.Role,
                    provider = u.Provider,
                    is_active = u.IsActive,
                    created_at = u.CreatedAt,
                    last_login_at = u.LastLoginAt,
                    avatar_style = u.AvatarStyle,
                    avatar_seed = u.AvatarSeed,
                    avatar = avatars.For(u)
                })
            });
        }).ExcludeFromDescription();

        // Who the sidebar is showing
        app.MapGet("/_/api/users/me", async (
            HttpContext ctx,
            AdminUserService users,
            PortwayApi.Services.Avatars.AvatarService avatars) =>
        {
            if (ctx.Items[SignedInUserKey] is not int id)
                return Results.Json(new { signed_in = false });

            var me = await users.FindByIdAsync(id);
            if (me is null) return Results.Json(new { signed_in = false });

            return Results.Json(new
            {
                signed_in = true,
                id = me.Id,
                username = me.Username,
                email = me.Email,
                display_name = me.DisplayName,
                role = me.Role,
                provider = me.Provider,
                has_password = me.PasswordHash.Length > 0,
                avatar = avatars.For(me)
            });
        }).ExcludeFromDescription();

        app.MapPost("/_/api/users", async (HttpContext ctx, AdminUserService users) =>
        {
            JsonElement body;
            try { body = await JsonSerializer.DeserializeAsync<JsonElement>(ctx.Request.Body); }
            catch (JsonException) { return PortwayResults.MinimalError("Invalid JSON body", 400); }

            if (await RefuseUnconfirmed(ctx, users, body) is { } refusal) return refusal;

            var username = body.TryGetProperty("username", out var u) ? u.GetString() : null;
            var password = body.TryGetProperty("password", out var p) ? p.GetString() : null;
            var role = body.TryGetProperty("role", out var r) ? r.GetString() ?? AdminUserRoles.Administrator
                                                              : AdminUserRoles.Administrator;

            if (AdminUserService.ValidateUsername(username) is { } nameError)
                return PortwayResults.MinimalError(nameError, 400, "username");
            if (AdminUserService.ValidatePassword(password) is { } passError)
                return PortwayResults.MinimalError(passError, 400, "password");
            if (!AdminUserRoles.IsKnown(role))
                return PortwayResults.MinimalError("Unknown role", 400, "role");

            if (await users.FindAsync(username!) is not null)
                return PortwayResults.MinimalError("That username is already taken", 409, "username");

            var (displayName, displayError) = AdminUserService.NormalizeDisplayName(
                body.TryGetProperty("display_name", out var dn) ? dn.GetString() : null);
            if (displayError is not null)
                return PortwayResults.MinimalError(displayError, 400, "display_name");

            var (avatar, avatarError) = ReadAvatar(body);
            if (avatarError is not null)
                return PortwayResults.MinimalError(avatarError, 400, "avatar");
            var (avatarStyle, avatarSeed) = avatar ?? Services.Avatars.AvatarService.Random();

            var email = body.TryGetProperty("email", out var e) ? e.GetString() ?? "" : "";
            var created = await users.CreateAsync(username!, password!, role, email, displayName, avatarStyle, avatarSeed);
            WebUiAuthState.Enabled = true;

            Audit(ctx, "create", username!, $"role {role}");
            Log.Information("Console account {Username} created", username);

            return Results.Json(new { ok = true, id = created.Id });
        }).ExcludeFromDescription();

        app.MapPut("/_/api/users/{id:int}", async (HttpContext ctx, int id, AdminUserService users) =>
        {
            JsonElement body;
            try { body = await JsonSerializer.DeserializeAsync<JsonElement>(ctx.Request.Body); }
            catch (JsonException) { return PortwayResults.MinimalError("Invalid JSON body", 400); }

            if (await RefuseUnconfirmed(ctx, users, body) is { } refusal) return refusal;

            var target = await users.FindByIdAsync(id);
            if (target is null) return PortwayResults.MinimalError("Account not found", 404);

            string? password = body.TryGetProperty("password", out var p) ? p.GetString() : null;
            string? role = body.TryGetProperty("role", out var r) ? r.GetString() : null;
            bool? isActive = body.TryGetProperty("is_active", out var a) && a.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? a.GetBoolean() : null;

            if (!string.IsNullOrEmpty(password) && AdminUserService.ValidatePassword(password) is { } passError)
                return PortwayResults.MinimalError(passError, 400, "password");
            if (role is not null && !AdminUserRoles.IsKnown(role))
                return PortwayResults.MinimalError("Unknown role", 400, "role");

            if (isActive == false && ctx.Items[SignedInUserKey] is int me && me == id)
                return PortwayResults.MinimalError("Another administrator must deactivate this account", 409, "is_active");

            // Never let the console lock itself out
            var losingAdmin = (role is not null && role != AdminUserRoles.Administrator) || isActive == false;
            if (losingAdmin && target.Role == AdminUserRoles.Administrator && await users.IsLastAdministratorAsync(id))
                return PortwayResults.MinimalError("This is the last active administrator", 409);

            string? displayName = null;
            if (body.TryGetProperty("display_name", out var dn))
            {
                (displayName, var displayError) = AdminUserService.NormalizeDisplayName(dn.GetString());
                if (displayError is not null)
                    return PortwayResults.MinimalError(displayError, 400, "display_name");
            }

            var (avatar, avatarError) = ReadAvatar(body);
            if (avatarError is not null)
                return PortwayResults.MinimalError(avatarError, 400, "avatar");

            string? email = body.TryGetProperty("email", out var e) ? e.GetString() : null;
            await users.UpdateAsync(id, password, role, isActive, email, displayName, avatar);

            Audit(ctx, "update", target.Username, string.Join(", ",
                new[] { password is not null ? "password" : null, role is not null ? $"role {role}" : null,
                        isActive is not null ? $"active {isActive}" : null }.Where(x => x is not null)));
            Log.Information("Console account {Username} updated", target.Username);

            return Results.Json(new { ok = true });
        }).ExcludeFromDescription();

        app.MapDelete("/_/api/users/{id:int}", async (HttpContext ctx, int id, AdminUserService users) =>
        {
            JsonElement body;
            try { body = await JsonSerializer.DeserializeAsync<JsonElement>(ctx.Request.Body); }
            catch (JsonException) { return PortwayResults.MinimalError("Invalid JSON body", 400); }

            if (await RefuseUnconfirmed(ctx, users, body) is { } refusal) return refusal;

            var target = await users.FindByIdAsync(id);
            if (target is null) return PortwayResults.MinimalError("Account not found", 404);

            if (ctx.Items[SignedInUserKey] is int me && me == id)
                return PortwayResults.MinimalError("You cannot delete the account you are signed in with", 409);

            if (target.Role == AdminUserRoles.Administrator && await users.IsLastAdministratorAsync(id))
                return PortwayResults.MinimalError("This is the last active administrator", 409);

            await users.DeleteAsync(id);
            WebUiAuthState.Enabled = await users.CountAsync() > 0;

            Audit(ctx, "delete", target.Username);
            Log.Information("Console account {Username} deleted", target.Username);

            return Results.Json(new { ok = true });
        }).ExcludeFromDescription();
    }

    // absent leaves the avatar unset, an empty style removes it, anything else must be an offered style and a safe seed
    private static ((string Style, string? Seed)? Avatar, string? Error) ReadAvatar(JsonElement body)
    {
        if (!body.TryGetProperty("avatar_style", out var styleElement)) return (null, null);
        var style = styleElement.ValueKind == JsonValueKind.String ? styleElement.GetString() : null;
        if (string.IsNullOrEmpty(style)) return (("", null), null);

        var seed = body.TryGetProperty("avatar_seed", out var seedElement) && seedElement.ValueKind == JsonValueKind.String
            ? seedElement.GetString() : null;
        if (!Services.Avatars.AvatarService.IsKnownStyle(style) || !Services.Avatars.AvatarService.IsValidSeed(seed))
            return (null, "Unknown avatar");
        return ((style, seed), null);
    }
}
