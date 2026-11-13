namespace PortwayApi.Endpoints;

using System.Reflection;
using System.Security.Cryptography;
using Serilog;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PortwayApi.Auth;
using PortwayApi.Classes;
using PortwayApi.Helpers;
using PortwayApi.Middleware;
using PortwayApi.Services;

public static partial class WebUiEndpointExtensions
{
    private const string CookieName = "portway_auth";
    private const string CsrfCookieName = "portway_csrf";
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);
    private static readonly TimeSpan RememberedSessionLifetime = TimeSpan.FromDays(30);
    private static readonly DateTime ProcessStartTime = DateTime.UtcNow;

    /// <summary>
    /// Registers console auth and the local network gate. Must run before UseStaticFiles.
    /// </summary>
    public static WebApplication UseWebUiAuth(this WebApplication app, bool webUiEnabled)
    {
        var publicOrigins = app.Configuration.GetSection("Console:PublicOrigins").Get<string[]>() ?? [];

        app.Use(async (context, next) =>
        {
            var path = context.Request.Path;
            if (!path.StartsWithSegments("/_")) { await next(); return; }
            if (!webUiEnabled)
            {
                Log.Warning("Web UI request to {Path} rejected: neither Console:Enabled nor Console:AdminApiKey is configured", path);
                context.Response.StatusCode = 503;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new { error = "Web UI is disabled" });
                return;
            }

            // public origins may reach the console, everything else must be local
            var isPublicOrigin = publicOrigins.Length > 0 && IsPublicOriginAllowed(context.Request, publicOrigins);

            if (!isPublicOrigin)
            {
                var remoteIp = context.Connection.RemoteIpAddress;
                var validator = context.RequestServices.GetRequiredService<UrlValidator>();

                if (remoteIp == null || !validator.IsClientIpAllowed(remoteIp))
                {
                    context.Response.StatusCode = 403;
                    context.Response.ContentType = "application/json";

                    await context.Response.WriteAsJsonAsync(new
                    {
                        error = "Access denied",
                        clientIp = remoteIp?.ToString() ?? "Unknown",
                        requestedPath = context.Request.Path.Value,
                        success = false
                    });

                    return;
                }
            }

            // read per request since accounts are seeded later
            if (WebUiAuthState.Enabled &&
                !path.StartsWithSegments("/_/login") &&
                !path.StartsWithSegments("/_/api/auth") &&
                !path.StartsWithSegments("/_/api/customization"))
            {
                var session = context.Request.Cookies.TryGetValue(CookieName, out var cookie)
                    ? WebUiAuthHelper.ResolveSession(cookie)
                    : null;

                if (session is not { } signedIn)
                {
                    context.Response.Redirect($"{context.Request.PathBase}/_/login");
                    return;
                }

                context.Items[SignedInUserKey] = signedIn.UserId;

                // recheck the account on every request so revocation is immediate
                var accounts = context.RequestServices.GetRequiredService<AdminUserService>();
                var account = await accounts.FindByIdAsync(signedIn.UserId);
                if (account is null || !account.IsActive || WebUiAuthHelper.SessionStamp(account.PasswordHash) != signedIn.Stamp)
                {
                    context.Response.Redirect($"{context.Request.PathBase}/_/login");
                    return;
                }
                context.Items[SignedInUsernameKey] = account.Username;
                context.Items[SignedInRoleKey] = account.Role;

                // csrf double submit on mutations, client-error uses sendBeacon and is exempt
                if (path.StartsWithSegments("/_/api") &&
                    !path.StartsWithSegments("/_/api/client-error") &&
                    (HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsPut(context.Request.Method) ||
                     HttpMethods.IsPatch(context.Request.Method) || HttpMethods.IsDelete(context.Request.Method)))
                {
                    var headerToken = context.Request.Headers["X-CSRF-Token"].FirstOrDefault();
                    context.Request.Cookies.TryGetValue(CsrfCookieName, out var csrfCookie);
                    if (string.IsNullOrEmpty(headerToken) || string.IsNullOrEmpty(csrfCookie) ||
                        !CryptographicOperations.FixedTimeEquals(
                            Encoding.UTF8.GetBytes(headerToken), Encoding.UTF8.GetBytes(csrfCookie)))
                    {
                        context.Response.StatusCode = 403;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsJsonAsync(new { error = "CSRF token missing or invalid" });
                        return;
                    }

                    // viewers may only change their own sign-in
                    if (!IsSelfServiceWrite(path) && account.Role != AdminUserRoles.Administrator)
                    {
                        Log.Warning("Console account {UserId} was refused {Method} {Path}: administrator role required",
                            signedIn.UserId, context.Request.Method, path.Value);
                        context.Response.StatusCode = 403;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsJsonAsync(new { error = "This action needs an administrator account" });
                        return;
                    }
                }
            }

            await next();
        });

        return app;
    }

    /// <summary>
    /// Writes any signed-in account may make about itself.
    /// </summary>
    private static bool IsSelfServiceWrite(PathString path)
    {
        if (path.StartsWithSegments("/_/api/client-error")) return true;
        if (path.StartsWithSegments("/_/api/oidc/link")) return true;

        // links this account to a provider
        return path.StartsWithSegments("/_/api/oidc/providers")
            && (path.Value ?? "").EndsWith("/link", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Matches the request origin against PublicOrigins. A wildcard matches one host label.
    /// </summary>
    internal static bool IsPublicOriginAllowed(HttpRequest request, string[] patterns)
    {
        // navigation requests include no origin header, fall back to scheme and host
        var origin = request.Headers.Origin.FirstOrDefault();
        if (string.IsNullOrEmpty(origin))
        {
            var scheme = request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? request.Scheme;
            origin = $"{scheme}://{request.Host.Value}";
        }

        return patterns.Any(p => MatchesOriginPattern(origin, p));
    }

    private static bool MatchesOriginPattern(string origin, string pattern)
    {
        if (!pattern.Contains('*'))
            return string.Equals(origin.TrimEnd('/'), pattern.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
        var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", "[^.]+") + "/?$";
        return Regex.IsMatch(origin, regexPattern, RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Maps all /_/* page routes and /_/api/* data endpoints
    /// </summary>
    public static WebApplication MapWebUiEndpoints(this WebApplication app)
    {
        var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot", "_");
        var appVersion = typeof(WebUiEndpointExtensions).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
        var secureCookies = app.Configuration.GetValue<bool>("Console:SecureCookies", false);

        // audit every config change and back up files before ui writes
        var configAudit = app.Services.GetRequiredService<PortwayApi.Services.Configuration.ConfigAuditService>();


        MapPageAndAuthRoutes(app, wwwroot, appVersion, secureCookies);
        MapInfoRoutes(app, appVersion);
        MapEnvironmentRoutes(app, configAudit);
        MapWebhookRoutes(app);
        MapSecretRoutes(app);
        MapSettingsRoutes(app, configAudit);
        MapUserRoutes(app, configAudit);
        MapOidcRoutes(app, configAudit, secureCookies);
        MapAuditRoutes(app, configAudit);
        MapTokenRoutes(app);
        MapRateLimitRoutes(app);
        MapEventRoutes(app);
        MapEndpointCrudRoutes(app, configAudit);
        MapDiagnosticsRoutes(app);
        MapMaintenanceRoutes(app, configAudit);

        return app;
    }

    /// <summary>
    /// The signed session, plus the CSRF cookie the pages echo back in X-CSRF-Token
    /// </summary>
    internal static void IssueSessionCookies(HttpContext context, AdminUser account, bool secureCookies, bool remember = false)
    {
        var lifetime = remember ? RememberedSessionLifetime : SessionLifetime;
        var expires = DateTimeOffset.UtcNow.Add(lifetime);

        context.Response.Cookies.Append(CookieName, WebUiAuthHelper.IssueSessionCookie(account.Id, account.PasswordHash, lifetime), new CookieOptions
        {
            HttpOnly = true,
            Secure = secureCookies,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = expires
        });

        // readable by page js so fetch can echo it
        context.Response.Cookies.Append(CsrfCookieName, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), new CookieOptions
        {
            HttpOnly = false,
            Secure = secureCookies,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = expires
        });
    }

    private static string? TypeFolder(string type) => type.ToLowerInvariant() switch
    {
        "sql" => "endpoints/SQL",
        "proxy" or "composite" => "endpoints/Proxy",
        "file" => "endpoints/Files",
        "static" => "endpoints/Static",
        "webhook" => "endpoints/Webhooks",
        _ => null
    };

    private static string? EndpointsDirectoryOf(string type) =>
        TypeFolder(type) is { } folder ? Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), folder)) : null;

    private static (string? filePath, string? error) ResolveEndpointPath(string type, string name)
    {
        // allow namespaced paths
        if (!Regex.IsMatch(name, @"^[a-zA-Z0-9_-]+(/[a-zA-Z0-9_-]+)*$"))
            return (null, "Invalid endpoint name");

        var baseDir = Directory.GetCurrentDirectory();
        var typeDir = TypeFolder(type);

        if (typeDir == null) return (null, $"Unknown endpoint type: {type}");

        var filePath = Path.GetFullPath(Path.Combine(baseDir, typeDir, name, "entity.json"));

        var allowedBase = Path.GetFullPath(Path.Combine(baseDir, typeDir));
        if (!filePath.StartsWith(allowedBase + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !filePath.Equals(allowedBase, StringComparison.OrdinalIgnoreCase))
            return (null, "Invalid path");

        // fall back to the leaf folder for namespaced names
        if (!File.Exists(filePath) && name.Contains('/') && !EndpointVersion.TryParse(name.Split('/')[^1], out _))
        {
            var leafName = name.Split('/')[^1];
            var fallbackPath = Path.GetFullPath(Path.Combine(baseDir, typeDir, leafName, "entity.json"));
            if (fallbackPath.StartsWith(allowedBase + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && File.Exists(fallbackPath))
                filePath = fallbackPath;
        }

        return (filePath, null);
    }

    private static EndpointType? TypeStringToEndpointType(string type) => type.ToLowerInvariant() switch
    {
        "sql" => EndpointType.SQL,
        "proxy" => EndpointType.Proxy,
        "composite" => EndpointType.Composite,
        "file" => EndpointType.Files,
        "static" => EndpointType.Static,
        "webhook" => EndpointType.Webhook,
        _ => null
    };

    /// <summary>
    /// Reloads endpoints after a console write; MCP and SQL metadata are refreshed here when the file watcher is off
    /// </summary>
    private static void ReloadEndpoints(IServiceProvider services, EndpointType? type)
    {
        if (type is null)
            return;

        EndpointHandler.ReloadEndpointType(type.Value);
        if (services.GetRequiredService<IOptionsMonitor<Services.Configuration.EndpointReloadingOptions>>().CurrentValue.Enabled)
            return;

        services.GetService<Services.Mcp.McpEndpointRegistry>()?.Refresh();
        if (type == EndpointType.SQL)
            services.GetRequiredService<SqlMetadataService>().QueueRefresh();
    }

    internal const string SignedInUserKey = "portway.user";
    internal const string SignedInUsernameKey = "portway.username";
    internal const string SignedInRoleKey = "portway.role";

    /// <summary>
    /// Composes a page from the shared shell, its view fragment and the footer, then applies the standard post-processing
    /// </summary>
    private static IResult ServeComposedPage(string wwwroot, string page, string title, HttpContext context, string version)
    {
        var shellPath = Path.Combine(wwwroot, "_shell.html");
        var viewPath = Path.Combine(wwwroot, "views", $"{page}.html");
        var footerPath = Path.Combine(wwwroot, "_footer.html");
        if (!File.Exists(shellPath) || !File.Exists(viewPath)) return Results.NotFound();

        var html = File.ReadAllText(shellPath)
            .Replace("<!-- PAGE_TITLE -->", $"{title} · Portway")
            + File.ReadAllText(viewPath)
            + (File.Exists(footerPath) ? File.ReadAllText(footerPath) : "\n</body>\n</html>\n");

        return FinishHtml(html, context, version);
    }

    private static IResult ServeHtml(string filePath, HttpContext context, string version, IConfiguration? config = null)
    {
        if (!File.Exists(filePath)) return Results.NotFound();
        var html = File.ReadAllText(filePath);
        if (filePath.EndsWith("login.html") && config != null)
        {
            var footerMd = config.GetValue<string>("Console:Customization:LoginFooter");
            if (!string.IsNullOrEmpty(footerMd))
            {
                var footerHtml = ParseMarkdownToHtml(footerMd);
                html = html.Replace("<!-- LOGIN_FOOTER_PLACEHOLDER -->",
                    $"<div class=\"signin-footer\">{footerHtml}</div>");
            }
        }

        return FinishHtml(html, context, version);
    }

    /// <summary>
    /// Shared post-processing: base href + PortwayBase injection, script nonces and cache-busting of local assets
    /// </summary>
    private static IResult FinishHtml(string html, HttpContext context, string version)
    {
        var pb = context.Request.PathBase.Value ?? "";
        var v = Uri.EscapeDataString(version);
        html = html.Replace("<head>", $"<head>\n  <base href=\"{pb}/\">\n  <script>window.PortwayBase=\"{pb}\";</script>");
        html = Regex.Replace(
            html,
            @"(href|src)=""(?!https?://)([^""]+\.(css|js))""",
            m => $"{m.Groups[1]}=\"{m.Groups[2]}?v={v}\"");
        return Results.Content(CspNonce.Apply(html, context), "text/html; charset=utf-8");
    }

    private static string ParseMarkdownToHtml(string md) =>
        MarkdownParser.ParseMarkdownToHtml(md);
}
