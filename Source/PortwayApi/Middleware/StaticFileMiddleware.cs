using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Serilog;

namespace PortwayApi.Middleware;

/// <summary>
/// Extension methods for configuring static files and routing middleware
/// </summary>
public static class StaticFilesMiddlewareExtensions
{
    /// <summary>
    /// Configures static file serving with proper caching and security headers
    /// </summary>
    public static IApplicationBuilder UseStaticFilesWithFallback(this IApplicationBuilder app, IWebHostEnvironment env)
    {
        // Configure static files with proper caching headers
        var staticFileOptions = new StaticFileOptions
        {
            OnPrepareResponse = context =>
            {
                var path = context.Context.Request.Path.Value?.ToLowerInvariant() ?? "";

                // Set appropriate cache headers based on file type
                if (path.EndsWith(".html") || path.EndsWith(".htm"))
                {
                    // Short cache for HTML files to allow updates
                    context.Context.Response.Headers.CacheControl = "public, max-age=300"; // 5 minutes
                    Log.Debug("📄 Serving HTML file: {Path}", path);
                }
                else if (path.EndsWith(".js") || path.EndsWith(".css"))
                {
                    // Longer cache for static assets
                    context.Context.Response.Headers.CacheControl = "public, max-age=3600"; // 1 hour
                }
                else if (path.EndsWith(".png") || path.EndsWith(".jpg") || path.EndsWith(".jpeg") ||
                        path.EndsWith(".gif") || path.EndsWith(".ico") || path.EndsWith(".svg"))
                {
                    // Long cache for images
                    context.Context.Response.Headers.CacheControl = "public, max-age=86400"; // 24 hours
                }
                else
                {
                    // Default cache for other static files
                    context.Context.Response.Headers.CacheControl = "public, max-age=1800"; // 30 minutes
                }

                // Add security headers for static files
                context.Context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            }
        };

        // Enable static file serving
        app.UseStaticFiles(staticFileOptions);

        // Add custom middleware to handle root path redirects
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";

            // Handle root path redirect
            if (path == "/")
            {
                Log.Information("📍 Root path accessed, checking for index.html or redirecting to swagger");

                // Check if index.html exists in wwwroot
                var webRootPath = env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                var indexPath = Path.Combine(webRootPath, "index.html");

                if (File.Exists(indexPath))
                {
                    // Check if the request accepts HTML (browser request)
                    var acceptHeader = context.Request.Headers.Accept.ToString();
                    if (acceptHeader.Contains("text/html") || string.IsNullOrEmpty(acceptHeader))
                    {
                        // For browser requests, serve the index.html file
                        context.Request.Path = "/index.html";
                        Log.Debug("📄 Rewriting root request to /index.html");
                        await next();
                        return;
                    }
                    else
                    {
                        // For API requests to root, redirect to openapi JSON
                        context.Response.Redirect("/docs/openapi/v1/openapi.json", permanent: false);
                        return;
                    }
                }
                else
                {
                    // Direct redirect to docs if no index.html
                    Log.Information("📖 No index.html found, redirecting to /docs");
                    context.Response.Redirect("/docs", permanent: false);
                    return;
                }
            }

            // Handle direct index.html requests
            if (path == "/index.html")
            {
                Log.Debug("📄 Direct index.html request");
            }

            // Handle swagger root redirect (legacy support)
            if (path == "/swagger" && !context.Request.Path.Value!.Contains("/swagger.json"))
            {
                Log.Debug("📖 Legacy Swagger UI redirect to new docs location");
                context.Response.Redirect("/docs/swagger", permanent: true);
                return;
            }

            // Handle docs paths
            if (path.StartsWith("/docs"))
            {
                Log.Debug("📖 Documentation accessed: {Path}", path);
            }

            await next();
        });

        Log.Debug("✅ Static files middleware configured with caching, security headers, and fallbacks");
        
        return app;
    }

    /// <summary>
    /// Configures default document options for serving index files
    /// </summary>
    public static IApplicationBuilder UseDefaultFilesWithOptions(this IApplicationBuilder app)
    {
        var defaultFilesOptions = new DefaultFilesOptions();
        defaultFilesOptions.DefaultFileNames.Clear();
        defaultFilesOptions.DefaultFileNames.Add("index.html");

        app.UseDefaultFiles(defaultFilesOptions);

        Log.Debug("✅ Default files configured (index.html)");
        return app;
    }

    /// <summary>
    /// Simple static files configuration without any custom logic (useful for testing)
    /// </summary>
    public static IApplicationBuilder UseBasicStaticFiles(this IApplicationBuilder app)
    {
        app.UseStaticFiles();
        Log.Debug("✅ Basic static files configured");
        return app;
    }

    /// <summary>
    /// Configuration for static files with just caching headers (no redirect logic)
    /// </summary>
    public static IApplicationBuilder UseStaticFilesWithCaching(this IApplicationBuilder app)
    {
        var staticFileOptions = new StaticFileOptions
        {
            OnPrepareResponse = context =>
            {
                var path = context.Context.Request.Path.Value?.ToLowerInvariant() ?? "";

                // Set appropriate cache headers based on file type
                if (path.EndsWith(".html") || path.EndsWith(".htm"))
                {
                    context.Context.Response.Headers.CacheControl = "public, max-age=300"; // 5 minutes
                }
                else if (path.EndsWith(".js") || path.EndsWith(".css"))
                {
                    context.Context.Response.Headers.CacheControl = "public, max-age=3600"; // 1 hour
                }
                else if (path.EndsWith(".png") || path.EndsWith(".jpg") || path.EndsWith(".jpeg") ||
                        path.EndsWith(".gif") || path.EndsWith(".ico") || path.EndsWith(".svg"))
                {
                    context.Context.Response.Headers.CacheControl = "public, max-age=86400"; // 24 hours
                }
                else
                {
                    context.Context.Response.Headers.CacheControl = "public, max-age=1800"; // 30 minutes
                }

                // Add security headers for static files
                context.Context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            }
        };

        app.UseStaticFiles(staticFileOptions);
        Log.Debug("✅ Static files with caching configured");
        return app;
    }
}