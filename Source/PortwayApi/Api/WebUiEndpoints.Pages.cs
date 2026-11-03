namespace PortwayApi.Endpoints;

using Serilog;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PortwayApi.Auth;
using PortwayApi.Helpers;


public static partial class WebUiEndpointExtensions
{
    private static void MapPageAndAuthRoutes(WebApplication app, string wwwroot, string appVersion, bool secureCookies)
    {
        // Login
        app.MapGet("/_/login", (HttpContext ctx) =>
            WebUiAuthState.Enabled
                ? ServeHtml(Path.Combine(wwwroot, "login.html"), ctx.Request.PathBase, appVersion, app.Configuration)
                : Results.Redirect($"{ctx.Request.PathBase}/_/dashboard"))
            .ExcludeFromDescription();
        app.MapGet("/_/login.html", (HttpContext ctx) => Results.Redirect($"{ctx.Request.PathBase}/_/login"))
            .ExcludeFromDescription();

        // CSRF token endpoint
        app.MapGet("/_/api/auth/csrf", async (PortwayApi.Auth.AuthDbContext db) => Results.Json(new
        {
            csrf = WebUiAuthHelper.GenerateCsrfToken(),
            // no account has a permanent password yet
            setup_pending = await db.AdminUsers.AnyAsync() && !await db.AdminUsers.AnyAsync(u => !u.MustChangePassword)
        }))
            .ExcludeFromDescription();

        // Auth endpoints
        app.MapPost("/_/api/auth", async (HttpContext context) =>
        {
            var body = await context.Request.ReadFromJsonAsync<JsonElement>();
            var username = body.TryGetProperty("username", out var up) ? up.GetString() ?? "" : "";
            var password = body.TryGetProperty("password", out var pp) ? pp.GetString() ?? "" : "";

            // Rate limiting and lockout check
            var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var blockReason = WebUiAuthHelper.BeginAttempt(clientIp, username);
            if (blockReason != null)
            {
                return Results.Json(new { error = blockReason }, statusCode: 429);
            }

            // CSRF validation
            var csrfToken = body.TryGetProperty("csrf", out var csrf) ? csrf.GetString() : null;
            if (!WebUiAuthHelper.ValidateCsrfToken(csrfToken))
            {
                return Results.Json(new { error = "Invalid or expired CSRF token" }, statusCode: 403);
            }

            var users = context.RequestServices.GetRequiredService<AdminUserService>();
            var account = await users.AuthenticateAsync(username, password);
            if (account is null)
            {
                Log.Warning("Failed console sign-in for {Username} from {ClientIp}", username, clientIp);
                return Results.Json(new { error = "Invalid username or password" }, statusCode: 401);
            }

            // Success - clear failed attempts
            WebUiAuthHelper.ClearFailedAttempts(clientIp, username);

            // No session until the account sets its own password
            if (account.MustChangePassword)
                return Results.Json(new { ok = false, must_change_password = true });

            // Consume the CSRF token (one-time use)
            WebUiAuthHelper.ConsumeCsrfToken(csrfToken!);

            IssueSessionCookies(context, account.Id, secureCookies);
            return Results.Ok(new { ok = true });
        }).ExcludeFromDescription();

        // Completes a first sign-in: verifies the old password again and issues the session only once the new one is set
        app.MapPost("/_/api/auth/password", async (HttpContext context) =>
        {
            var body = await context.Request.ReadFromJsonAsync<JsonElement>();

            var username = body.TryGetProperty("username", out var u) ? u.GetString() ?? "" : "";
            var current = body.TryGetProperty("password", out var p) ? p.GetString() ?? "" : "";
            var next = body.TryGetProperty("newPassword", out var n) ? n.GetString() ?? "" : "";

            // policy errors reveal nothing about the account and do not count toward the lockout
            if (AdminUserService.ValidatePassword(next) is { } problem)
                return Results.Json(new { error = problem, field = "newPassword" }, statusCode: 400);
            if (next == current)
                return Results.Json(new { error = "Choose a password you have not used here before", field = "newPassword" }, statusCode: 400);

            var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            if (WebUiAuthHelper.BeginAttempt(clientIp, username) is { } blocked)
                return Results.Json(new { error = blocked }, statusCode: 429);

            var csrf = body.TryGetProperty("csrf", out var c) ? c.GetString() : null;
            if (!WebUiAuthHelper.ValidateCsrfToken(csrf))
            {
                return Results.Json(new { error = "Invalid or expired CSRF token" }, statusCode: 403);
            }

            var users = context.RequestServices.GetRequiredService<AdminUserService>();
            if (!await users.ChangePasswordAsync(username, current, next))
            {
                return Results.Json(new { error = "Invalid username or password" }, statusCode: 401);
            }

            WebUiAuthHelper.ClearFailedAttempts(clientIp, username);
            WebUiAuthHelper.ConsumeCsrfToken(csrf!);

            var account = await users.FindAsync(username);
            Log.Information("Console account {Username} set its own password", username);

            IssueSessionCookies(context, account!.Id, secureCookies);
            return Results.Ok(new { ok = true });
        }).ExcludeFromDescription();

        app.MapPost("/_/api/auth/logout", (HttpContext context) =>
        {
            context.Response.Cookies.Append(CookieName, "", new CookieOptions
            {
                HttpOnly = true,
                Secure = secureCookies,
                SameSite = SameSiteMode.Lax,
                Path = "/",
                Expires = DateTimeOffset.UnixEpoch
            });
            context.Response.Cookies.Append(CsrfCookieName, "", new CookieOptions
            {
                HttpOnly = false,
                Secure = secureCookies,
                SameSite = SameSiteMode.Lax,
                Path = "/",
                Expires = DateTimeOffset.UnixEpoch
            });
            return Results.Ok();
        }).ExcludeFromDescription();

        // Page routes
        app.MapGet("/_", (HttpContext ctx) => Results.Redirect($"{ctx.Request.PathBase}/_/dashboard"))
            .ExcludeFromDescription();

        // Pages render Beacon-style: _shell.html + views/{page}.html + _footer.html streamed as one document
        var pageTitles = new Dictionary<string, string>
        {
            ["dashboard"] = "Dashboard",
            ["health"] = "Health",
            ["endpoints"] = "Endpoints",
            ["environments"] = "Environments",
            ["tokens"] = "Access Tokens",
            ["users"] = "Users",
            ["settings"] = "Settings",
            ["logs"] = "Logs",
            ["trace"] = "Trace"
        };
        foreach (var (page, title) in pageTitles)
        {
            var p = page;
            var t = title;
            app.MapGet($"/_/{p}", (HttpContext ctx) => ServeComposedPage(wwwroot, p, t, ctx.Request.PathBase, appVersion)).ExcludeFromDescription();
            app.MapGet($"/_/{p}.html", (HttpContext ctx) => Results.Redirect($"{ctx.Request.PathBase}/_/{p}")).ExcludeFromDescription();
        }

        // MCP explorer page
        var mcpExplorerPath = Path.Combine(wwwroot, "mcp", "explorer.html");
        app.MapGet("/_/mcp/explorer", (HttpContext ctx) => ServeHtml(mcpExplorerPath, ctx.Request.PathBase, appVersion, app.Configuration)).ExcludeFromDescription();
        app.MapGet("/_/mcp/explorer.html", (HttpContext ctx) => Results.Redirect($"{ctx.Request.PathBase}/_/mcp/explorer")).ExcludeFromDescription();

        // MCP chat page
        var mcpChatPath = Path.Combine(wwwroot, "mcp", "chat.html");
        app.MapGet("/_/mcp", (HttpContext ctx) => ServeHtml(mcpChatPath, ctx.Request.PathBase, appVersion, app.Configuration)).ExcludeFromDescription();
        app.MapGet("/_/mcp/chat", (HttpContext ctx) => ServeHtml(mcpChatPath, ctx.Request.PathBase, appVersion, app.Configuration)).ExcludeFromDescription();
        app.MapGet("/_/mcp/chat.html", (HttpContext ctx) => Results.Redirect($"{ctx.Request.PathBase}/_/mcp/chat")).ExcludeFromDescription();

        // Data endpoints
    }
}
