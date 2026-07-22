namespace PortwayApi.Endpoints;

using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using Serilog;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PortwayApi.Auth;
using PortwayApi.Classes;
using PortwayApi.Helpers;
using PortwayApi.Interfaces;
using PortwayApi.Services;


public static partial class WebUiEndpointExtensions
{
    private static void MapPageAndAuthRoutes(WebApplication app, string adminApiKey, bool uiAuthEnabled, string wwwroot, string appVersion, bool secureCookies)
    {
        // Login
        app.MapGet("/ui/login", (HttpContext ctx) =>
            uiAuthEnabled
                ? ServeHtml(Path.Combine(wwwroot, "login.html"), ctx.Request.PathBase, appVersion, app.Configuration)
                : Results.Redirect($"{ctx.Request.PathBase}/ui/dashboard"))
            .ExcludeFromDescription();
        app.MapGet("/ui/login.html", (HttpContext ctx) => Results.Redirect($"{ctx.Request.PathBase}/ui/login"))
            .ExcludeFromDescription();

        // CSRF token endpoint
        app.MapGet("/ui/api/auth/csrf", () => Results.Json(new { csrf = WebUiAuthHelper.GenerateCsrfToken() }))
            .ExcludeFromDescription();

        // Auth endpoints
        app.MapPost("/ui/api/auth", async (HttpContext context) =>
        {
            // Rate limiting and lockout check
            var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var blockReason = WebUiAuthHelper.CheckAccess(clientIp);
            if (blockReason != null)
            {
                return Results.Json(new { error = blockReason }, statusCode: 429);
            }

            var body = await context.Request.ReadFromJsonAsync<JsonElement>();
            
            // CSRF validation
            var csrfToken = body.TryGetProperty("csrf", out var csrf) ? csrf.GetString() : null;
            if (!WebUiAuthHelper.ValidateCsrfToken(csrfToken))
            {
                WebUiAuthHelper.RecordFailedAttempt(clientIp);
                return Results.Json(new { error = "Invalid or expired CSRF token" }, statusCode: 403);
            }
            
            var provided = body.TryGetProperty("apiKey", out var kp) ? kp.GetString() ?? "" : "";
            var provHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
            var expHash  = SHA256.HashData(Encoding.UTF8.GetBytes(adminApiKey));
            if (!CryptographicOperations.FixedTimeEquals(provHash, expHash))
            {
                WebUiAuthHelper.RecordFailedAttempt(clientIp);
                return Results.Json(new { error = "Invalid API key" }, statusCode: 401);
            }

            // Success - clear failed attempts
            WebUiAuthHelper.ClearFailedAttempts(clientIp);
            // Consume the CSRF token (one-time use)
            WebUiAuthHelper.ConsumeCsrfToken(csrfToken!);

            var token = GenerateToken(adminApiKey);
            context.Response.Cookies.Append(CookieName, token, new CookieOptions
            {
                HttpOnly = true,
                Secure = secureCookies, // Configurable - set to true in production with HTTPS
                SameSite = SameSiteMode.Lax,
                Path     = "/",
                Expires  = DateTimeOffset.UtcNow.AddHours(TokenExpiryHours)
            });
            // Session CSRF cookie for the double-submit check; readable by page JS so fetches can echo it in X-CSRF-Token
            var sessionCsrf = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            context.Response.Cookies.Append(CsrfCookieName, sessionCsrf, new CookieOptions
            {
                HttpOnly = false,
                Secure = secureCookies,
                SameSite = SameSiteMode.Lax,
                Path     = "/",
                Expires  = DateTimeOffset.UtcNow.AddHours(TokenExpiryHours)
            });
            return Results.Ok(new { ok = true });
        }).ExcludeFromDescription();

        app.MapPost("/ui/api/auth/logout", (HttpContext context) =>
        {
            context.Response.Cookies.Append(CookieName, "", new CookieOptions
            {
                HttpOnly = true,
                Secure = secureCookies,
                SameSite = SameSiteMode.Lax,
                Path     = "/",
                Expires  = DateTimeOffset.UnixEpoch
            });
            context.Response.Cookies.Append(CsrfCookieName, "", new CookieOptions
            {
                HttpOnly = false,
                Secure = secureCookies,
                SameSite = SameSiteMode.Lax,
                Path     = "/",
                Expires  = DateTimeOffset.UnixEpoch
            });
            return Results.Ok();
        }).ExcludeFromDescription();

        // Page routes
        app.MapGet("/ui", (HttpContext ctx) => Results.Redirect($"{ctx.Request.PathBase}/ui/dashboard"))
            .ExcludeFromDescription();

        // Pages render Beacon-style: _shell.html + views/{page}.html + _footer.html streamed as one document
        var pageTitles = new Dictionary<string, string>
        {
            ["dashboard"]    = "Dashboard",
            ["endpoints"]    = "Endpoints",
            ["environments"] = "Environments",
            ["tokens"]       = "Access Tokens",
            ["settings"]     = "Settings",
            ["logs"]         = "Logs"
        };
        foreach (var (page, title) in pageTitles)
        {
            var p = page;
            var t = title;
            app.MapGet($"/ui/{p}",      (HttpContext ctx) => ServeComposedPage(wwwroot, p, t, ctx.Request.PathBase, appVersion)).ExcludeFromDescription();
            app.MapGet($"/ui/{p}.html", (HttpContext ctx) => Results.Redirect($"{ctx.Request.PathBase}/ui/{p}")).ExcludeFromDescription();
        }

        // MCP explorer page
        var mcpExplorerPath = Path.Combine(wwwroot, "mcp", "explorer.html");
        app.MapGet("/ui/mcp/explorer", (HttpContext ctx) => ServeHtml(mcpExplorerPath, ctx.Request.PathBase, appVersion, app.Configuration)).ExcludeFromDescription();
        app.MapGet("/ui/mcp/explorer.html", (HttpContext ctx) => Results.Redirect($"{ctx.Request.PathBase}/ui/mcp/explorer")).ExcludeFromDescription();

        // MCP chat page
        var mcpChatPath = Path.Combine(wwwroot, "mcp", "chat.html");
        app.MapGet("/ui/mcp", (HttpContext ctx) => ServeHtml(mcpChatPath, ctx.Request.PathBase, appVersion, app.Configuration)).ExcludeFromDescription();
        app.MapGet("/ui/mcp/chat", (HttpContext ctx) => ServeHtml(mcpChatPath, ctx.Request.PathBase, appVersion, app.Configuration)).ExcludeFromDescription();
        app.MapGet("/ui/mcp/chat.html", (HttpContext ctx) => Results.Redirect($"{ctx.Request.PathBase}/ui/mcp/chat")).ExcludeFromDescription();

        // Data endpoints
    }
}
