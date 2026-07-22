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
    private const string CookieName = "portway_auth";
    private const string CsrfCookieName = "portway_csrf";
    private const int TokenExpiryHours = 12;
    private static readonly DateTime ProcessStartTime = DateTime.UtcNow;

    /// <summary>Registers the UI authorz. and local network-only middleware. To not make my same mistake twice: must be called before UseStaticFiles...</summary>
    public static WebApplication UseWebUiAuth(this WebApplication app, string adminApiKey)
    {
        var uiAuthEnabled = !string.IsNullOrEmpty(adminApiKey);
        var publicOrigins = app.Configuration.GetSection("WebUi:PublicOrigins").Get<string[]>() ?? [];

        app.Use(async (context, next) =>
        {
            var path = context.Request.Path;
            if (!path.StartsWithSegments("/ui")) { await next(); return; }

            // Allow external clients whose origin matches a configured PublicOrigins pattern; Otherwise restrict to local network only
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

            // Cookie auth check, skip for the login page and the auth endpoints themselves
            if (uiAuthEnabled &&
                !path.StartsWithSegments("/ui/login") &&
                !path.StartsWithSegments("/ui/api/auth") &&
                !path.StartsWithSegments("/ui/api/customization"))
            {
                if (!context.Request.Cookies.TryGetValue(CookieName, out var cookie) ||
                    !ValidateToken(cookie, adminApiKey))
                {
                    context.Response.Redirect($"{context.Request.PathBase}/ui/login");
                    return;
                }

                // CSRF double-submit check on mutating UI API calls; client-error is sendBeacon and cannot set headers
                if (path.StartsWithSegments("/ui/api") &&
                    !path.StartsWithSegments("/ui/api/client-error") &&
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
                }
            }

            await next();
        });

        return app;
    }

    /// <summary>Returns true if the request's effective origin matches any of the configured PublicOrigins patterns. Patterns support a single wildcard (*) per segment, e.g. "https://*.melosso.com"</summary>
    internal static bool IsPublicOriginAllowed(HttpRequest request, string[] patterns)
    {
        // Origin header is present on XHR/fetch; for navigation requests fall back to scheme+host
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

        // * matches a single host label (no dots), e.g. https://*.melosso.com matches
        // https://foo.melosso.com but NOT https://a.b.melosso.com
        var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", "[^.]+") + "/?$";
        return Regex.IsMatch(origin, regexPattern, RegexOptions.IgnoreCase);
    }

    /// <summary>Maps all /ui/* page routes and /ui/api/* data endpoints</summary>
    public static WebApplication MapWebUiEndpoints(this WebApplication app, string adminApiKey)
    {
        var uiAuthEnabled = !string.IsNullOrEmpty(adminApiKey);
        var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot", "ui");
        var appVersion = typeof(WebUiEndpointExtensions).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";

        // Get security settings from configuration
        var secureCookies = app.Configuration.GetValue<bool>("WebUi:SecureCookies", false);

        // Change-controls: audit every config mutation, back up files before UI writes
        var configAudit = app.Services.GetRequiredService<PortwayApi.Services.Configuration.ConfigAuditService>();
        

        MapPageAndAuthRoutes(app, adminApiKey, uiAuthEnabled, wwwroot, appVersion, secureCookies);
        MapInfoRoutes(app, appVersion);
        MapEnvironmentRoutes(app, configAudit);
        MapSettingsRoutes(app, configAudit);
        MapAuditRoutes(app, configAudit);
        MapTokenRoutes(app);
        MapEventRoutes(app);
        MapEndpointCrudRoutes(app, configAudit);
        MapDiagnosticsRoutes(app);

        return app;
    }

    private static string GenerateToken(string adminApiKey)
    {
        var expiry     = DateTimeOffset.UtcNow.AddHours(TokenExpiryHours).ToUnixTimeSeconds().ToString();
        var signingKey = SHA256.HashData(Encoding.UTF8.GetBytes(adminApiKey));
        using var hmac = new HMACSHA256(signingKey);
        var sig        = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(expiry)));
        return $"{expiry}.{sig}";
    }

    private static (string? filePath, string? error) ResolveEndpointPath(string type, string name)
    {
        // Allow namespace/name paths
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z0-9_-]+(/[a-zA-Z0-9_-]+)*$"))
            return (null, "Invalid endpoint name");

        var baseDir = Directory.GetCurrentDirectory();
        var (typeDir, isFixed) = type.ToLowerInvariant() switch
        {
            "sql"       => ("endpoints/SQL",      false),
            "proxy"     => ("endpoints/Proxy",    false),
            "composite" => ("endpoints/Proxy",    false),
            "file"      => ("endpoints/Files",    false),
            "static"    => ("endpoints/Static",   false),
            "webhook"   => ("endpoints/Webhooks", false),
            _           => ((string?)null,        false)
        };

        if (typeDir == null) return (null, $"Unknown endpoint type: {type}");

        var filePath = isFixed
            ? Path.GetFullPath(Path.Combine(baseDir, typeDir, "entity.json"))
            : Path.GetFullPath(Path.Combine(baseDir, typeDir, name, "entity.json"));

        var allowedBase = Path.GetFullPath(Path.Combine(baseDir, typeDir));
        if (!filePath.StartsWith(allowedBase + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !filePath.Equals(allowedBase, StringComparison.OrdinalIgnoreCase))
            return (null, "Invalid path");

        // Fallback: if path doesn't exist and name is namespaced (e.g. NS/Name), retry with just the
        // leaf folder; handles the doubled-key case where Namespace == folder name (e.g. Production/Production → Production/)
        // and the explicit-namespace case (e.g. Catalog/Products → Products/)
        if (!isFixed && !File.Exists(filePath) && name.Contains('/'))
        {
            var leafName     = name.Split('/')[^1];
            var fallbackPath = Path.GetFullPath(Path.Combine(baseDir, typeDir, leafName, "entity.json"));
            if (fallbackPath.StartsWith(allowedBase + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && File.Exists(fallbackPath))
                filePath = fallbackPath;
        }

        return (filePath, null);
    }

    private static EndpointType? TypeStringToEndpointType(string type) => type.ToLowerInvariant() switch
    {
        "sql"       => EndpointType.SQL,
        "proxy"     => EndpointType.Proxy,
        "composite" => EndpointType.Composite,
        "file"      => EndpointType.Files,
        "static"    => EndpointType.Static,
        "webhook"   => EndpointType.Webhook,
        _           => null
    };

    private static bool ValidateToken(string token, string adminApiKey)
        => WebUiAuthHelper.IsValidSessionCookie(token, adminApiKey);

    /// <summary>Composes a page from the shared shell, its view fragment and the footer, then applies the standard post-processing</summary>
    private static IResult ServeComposedPage(string wwwroot, string page, string title, PathString pathBase, string version)
    {
        var shellPath  = Path.Combine(wwwroot, "_shell.html");
        var viewPath   = Path.Combine(wwwroot, "views", $"{page}.html");
        var footerPath = Path.Combine(wwwroot, "_footer.html");
        if (!File.Exists(shellPath) || !File.Exists(viewPath)) return Results.NotFound();

        var html = File.ReadAllText(shellPath)
            .Replace("<!-- PAGE_TITLE -->", $"{title} · Portway")
            + File.ReadAllText(viewPath)
            + (File.Exists(footerPath) ? File.ReadAllText(footerPath) : "\n</body>\n</html>\n");

        return FinishHtml(html, pathBase, version);
    }

    private static IResult ServeHtml(string filePath, PathString pathBase, string version, IConfiguration? config = null)
    {
        if (!File.Exists(filePath)) return Results.NotFound();
        var html = File.ReadAllText(filePath);

        // Inject Login Footer if this is the login page
        if (filePath.EndsWith("login.html") && config != null)
        {
            var footerMd = config.GetValue<string>("WebUi:Customization:LoginFooter");
            if (!string.IsNullOrEmpty(footerMd))
            {
                var footerHtml = ParseMarkdownToHtml(footerMd);
                html = html.Replace("<!-- LOGIN_FOOTER_PLACEHOLDER -->",
                    $"<div class=\"auth-footer\">{footerHtml}</div>");
            }
        }

        return FinishHtml(html, pathBase, version);
    }

    /// <summary>Shared post-processing: base href + PortwayBase injection and cache-busting of local assets</summary>
    private static IResult FinishHtml(string html, PathString pathBase, string version)
    {
        var pb = pathBase.Value ?? "";
        var v  = Uri.EscapeDataString(version);
        html = html.Replace("<head>", $"<head>\n  <base href=\"{pb}/\">\n  <script>window.PortwayBase=\"{pb}\";</script>");
        html = System.Text.RegularExpressions.Regex.Replace(
            html,
            @"(href|src)=""(?!https?://)([^""]+\.(css|js))""",
            m => $"{m.Groups[1]}=\"{m.Groups[2]}?v={v}\"");
        return Results.Content(html, "text/html; charset=utf-8");
    }

    private static string ParseMarkdownToHtml(string md) =>
        PortwayApi.Helpers.MarkdownParser.ParseMarkdownToHtml(md);
}
