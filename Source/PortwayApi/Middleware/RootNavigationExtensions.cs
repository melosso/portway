namespace PortwayApi.Middleware;

using PortwayApi.Endpoints;
using PortwayApi.Helpers;
using Serilog;

/// <summary>Path base setup, index.html path base injection and root path redirects</summary>
public static class RootNavigationExtensions
{
    /// <summary>Applies ASPNETCORE_PATHBASE or PathBase config when set; no-op otherwise</summary>
    public static WebApplication UsePortwayPathBase(this WebApplication app)
    {
        var pathBase = Environment.GetEnvironmentVariable("ASPNETCORE_PATHBASE")
            ?? app.Configuration["PathBase"];

        if (string.IsNullOrEmpty(pathBase))
            return app;

        // Ensure path starts with /
        if (!pathBase.StartsWith("/"))
        {
            pathBase = "/" + pathBase;
        }

        app.UsePathBase(pathBase);
        Log.Information("Application configured with path base: {PathBase}", pathBase);

        // Debug logging for path base handling during internal routing
        app.Use((context, next) =>
        {
            if (context.Request.PathBase.HasValue)
            {
                Log.Debug("Request PathBase: {PathBase}, Path: {Path}",
                    context.Request.PathBase, context.Request.Path);
            }
            return next();
        });

        return app;
    }

    /// <summary>Injects base href and window.PortwayBase into index.html before static files serve it</summary>
    public static WebApplication UseIndexHtmlPathBaseInjection(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.Value?.Equals("/index.html", StringComparison.OrdinalIgnoreCase) == true)
            {
                var webRoot = app.Environment.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                var filePath = Path.Combine(webRoot, "index.html");
                if (File.Exists(filePath))
                {
                    var pb = context.Request.PathBase.Value ?? "";
                    var html = await File.ReadAllTextAsync(filePath);
                    html = html.Replace("<head>", $"<head>\n  <base href=\"{pb}/\">\n  <script>window.PortwayBase=\"{pb}\";</script>");
                    context.Response.ContentType = "text/html; charset=utf-8";
                    await context.Response.WriteAsync(html);
                    return;
                }
            }
            await next();
        });

        return app;
    }

    /// <summary>Root path and legacy /swagger redirects; landing page for local or allowed public origins</summary>
    public static WebApplication UsePortwayRootRedirects(
        this WebApplication app,
        string adminApiKey,
        string[] publicOrigins,
        bool enableLandingPage)
    {
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";
            var pathBase = context.Request.PathBase.Value ?? "";

            Log.Debug("Incoming request: PathBase={PathBase}, Path={Path}", pathBase, path);

            // Handle root path redirect
            if (path == "/" || path == "")
            {
                var acceptHeader = context.Request.Headers.Accept.ToString();
                var isHtmlRequest = acceptHeader.Contains("text/html") || string.IsNullOrEmpty(acceptHeader);

                if (!isHtmlRequest)
                {
                    // Non-browser requests always get the OpenAPI JSON
                    var redirectPath = $"{pathBase}/docs/openapi/v1/openapi.json";
                    Log.Debug("API root request, redirecting to {Path}", redirectPath);
                    context.Response.Redirect(redirectPath, permanent: false);
                    return;
                }

                // Browser request: landing page for local clients or allowed PublicOrigins; others go straight to docs
                var remoteIp = context.Connection.RemoteIpAddress;
                var urlValidator = context.RequestServices.GetRequiredService<UrlValidator>();
                var isLocalClient = remoteIp != null && urlValidator.IsClientIpAllowed(
                    remoteIp.IsIPv4MappedToIPv6 ? remoteIp.MapToIPv4() : remoteIp);
                var isPublicOrigin = publicOrigins.Length > 0 &&
                    WebUiEndpointExtensions.IsPublicOriginAllowed(context.Request, publicOrigins);

                if (enableLandingPage && (isLocalClient || isPublicOrigin) && !string.IsNullOrEmpty(adminApiKey))
                {
                    var webRootPath = app.Environment.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                    var indexPath = Path.Combine(webRootPath, "index.html");
                    if (File.Exists(indexPath))
                    {
                        Log.Debug("Local client at root, serving landing page");
                        context.Response.Redirect($"{pathBase}/index.html", permanent: false);
                        return;
                    }
                }

                // External client or no landing page; redirect to docs
                {
                    var redirectPath = $"{pathBase}/docs";
                    Log.Debug("Redirecting root to {Path}", redirectPath);
                    context.Response.Redirect(redirectPath, permanent: false);
                    return;
                }
            }

            // Handle now removed /swagger redirect (backward compatibility)
            if (path == "/swagger" && !context.Request.Path.Value!.Contains("/swagger.json"))
            {
                var redirectPath = $"{pathBase}/docs";
                Log.Debug("Legacy /swagger redirect to {Path}", redirectPath);
                context.Response.Redirect(redirectPath, permanent: true);
                return;
            }

            // Handle documentation paths (logging only)
            if (context.Request.Path.StartsWithSegments("/docs"))
            {
                Log.Debug("Documentation accessed: PathBase={PathBase}, Path={Path}",
                    pathBase, context.Request.Path.Value);
            }

            await next();
        });

        return app;
    }
}
