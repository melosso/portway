namespace PortwayApi.Endpoints;

using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
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
                return Results.Json(new { error = "Sign in to continue" }, statusCode: 401);

            var clientIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            if (WebUiAuthHelper.CheckAccess(clientIp) is { } blocked)
                return Results.Json(new { error = blocked }, statusCode: 429);

            var confirm = body.TryGetProperty("current_password", out var c) ? c.GetString() ?? "" : "";
            if (await users.ConfirmPasswordAsync(me, confirm))
            {
                WebUiAuthHelper.ClearFailedAttempts(clientIp);
                return null;
            }

            WebUiAuthHelper.RecordFailedAttempt(clientIp);
            Log.Warning("Account change refused: the password for the signed-in account was wrong, from {ClientIp}", clientIp);
            return Results.Json(new { error = "That password is incorrect", field = "current_password" }, statusCode: 403);
        }

        app.MapGet("/ui/api/users", async (
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
                    role = u.Role,
                    provider = u.Provider,
                    is_active = u.IsActive,
                    created_at = u.CreatedAt,
                    last_login_at = u.LastLoginAt,
                    avatar = avatars.DataUriFor(u.Username)
                })
            });
        }).ExcludeFromDescription();

        // Who the sidebar is showing
        app.MapGet("/ui/api/users/me", async (
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
                role = me.Role,
                provider = me.Provider,
                has_password = me.PasswordHash.Length > 0,
                avatar = avatars.DataUriFor(me.Username)
            });
        }).ExcludeFromDescription();

        app.MapPost("/ui/api/users", async (HttpContext ctx, AdminUserService users) =>
        {
            JsonElement body;
            try { body = await JsonSerializer.DeserializeAsync<JsonElement>(ctx.Request.Body); }
            catch (JsonException) { return Results.Json(new { error = "Invalid JSON body" }, statusCode: 400); }

            if (await RefuseUnconfirmed(ctx, users, body) is { } refusal) return refusal;

            var username = body.TryGetProperty("username", out var u) ? u.GetString() : null;
            var password = body.TryGetProperty("password", out var p) ? p.GetString() : null;
            var role = body.TryGetProperty("role", out var r) ? r.GetString() ?? AdminUserRoles.Administrator
                                                              : AdminUserRoles.Administrator;

            if (AdminUserService.ValidateUsername(username) is { } nameError)
                return Results.Json(new { error = nameError, field = "username" }, statusCode: 400);
            if (AdminUserService.ValidatePassword(password) is { } passError)
                return Results.Json(new { error = passError, field = "password" }, statusCode: 400);
            if (!AdminUserRoles.IsKnown(role))
                return Results.Json(new { error = "Unknown role", field = "role" }, statusCode: 400);

            if (await users.FindAsync(username!) is not null)
                return Results.Json(new { error = "That username is already taken", field = "username" }, statusCode: 409);

            var email = body.TryGetProperty("email", out var e) ? e.GetString() ?? "" : "";
            var created = await users.CreateAsync(username!, password!, role, email);
            WebUiAuthState.Enabled = true;

            Audit(ctx, "create", username!, $"role {role}");
            Log.Information("Console account {Username} created", username);

            return Results.Json(new { ok = true, id = created.Id });
        }).ExcludeFromDescription();

        app.MapPut("/ui/api/users/{id:int}", async (HttpContext ctx, int id, AdminUserService users) =>
        {
            JsonElement body;
            try { body = await JsonSerializer.DeserializeAsync<JsonElement>(ctx.Request.Body); }
            catch (JsonException) { return Results.Json(new { error = "Invalid JSON body" }, statusCode: 400); }

            if (await RefuseUnconfirmed(ctx, users, body) is { } refusal) return refusal;

            var target = await users.FindByIdAsync(id);
            if (target is null) return Results.Json(new { error = "Account not found" }, statusCode: 404);

            string? password = body.TryGetProperty("password", out var p) ? p.GetString() : null;
            string? role = body.TryGetProperty("role", out var r) ? r.GetString() : null;
            bool? isActive = body.TryGetProperty("is_active", out var a) && a.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? a.GetBoolean() : null;

            if (!string.IsNullOrEmpty(password) && AdminUserService.ValidatePassword(password) is { } passError)
                return Results.Json(new { error = passError, field = "password" }, statusCode: 400);
            if (role is not null && !AdminUserRoles.IsKnown(role))
                return Results.Json(new { error = "Unknown role", field = "role" }, statusCode: 400);

            // Never let the console lock itself out
            var losingAdmin = (role is not null && role != AdminUserRoles.Administrator) || isActive == false;
            if (losingAdmin && target.Role == AdminUserRoles.Administrator && await users.IsLastAdministratorAsync(id))
                return Results.Json(new { error = "This is the last active administrator" }, statusCode: 409);

            string? email = body.TryGetProperty("email", out var e) ? e.GetString() : null;
            await users.UpdateAsync(id, password, role, isActive, email);

            Audit(ctx, "update", target.Username, string.Join(", ",
                new[] { password is not null ? "password" : null, role is not null ? $"role {role}" : null,
                        isActive is not null ? $"active {isActive}" : null }.Where(x => x is not null)));
            Log.Information("Console account {Username} updated", target.Username);

            return Results.Json(new { ok = true });
        }).ExcludeFromDescription();

        app.MapDelete("/ui/api/users/{id:int}", async (HttpContext ctx, int id, AdminUserService users) =>
        {
            JsonElement body;
            try { body = await JsonSerializer.DeserializeAsync<JsonElement>(ctx.Request.Body); }
            catch (JsonException) { return Results.Json(new { error = "Invalid JSON body" }, statusCode: 400); }

            if (await RefuseUnconfirmed(ctx, users, body) is { } refusal) return refusal;

            var target = await users.FindByIdAsync(id);
            if (target is null) return Results.Json(new { error = "Account not found" }, statusCode: 404);

            if (ctx.Items[SignedInUserKey] is int me && me == id)
                return Results.Json(new { error = "You cannot delete the account you are signed in with" }, statusCode: 409);

            if (target.Role == AdminUserRoles.Administrator && await users.IsLastAdministratorAsync(id))
                return Results.Json(new { error = "This is the last active administrator" }, statusCode: 409);

            await users.DeleteAsync(id);
            WebUiAuthState.Enabled = await users.CountAsync() > 0;

            Audit(ctx, "delete", target.Username);
            Log.Information("Console account {Username} deleted", target.Username);

            return Results.Json(new { ok = true });
        }).ExcludeFromDescription();
    }
}
