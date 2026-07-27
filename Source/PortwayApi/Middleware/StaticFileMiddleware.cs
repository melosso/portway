using System.IO;
using Microsoft.AspNetCore.Builder;
using PortwayApi.Helpers;
using Serilog;

namespace PortwayApi.Middleware;

/// <summary>Extension methods for configuring static files and routing middleware</summary>
public static class StaticFilesMiddlewareExtensions
{
    /// <summary>Configures default document options for serving index files</summary>
    public static IApplicationBuilder UseDefaultFilesWithOptions(this IApplicationBuilder app)
    {
        var defaultFilesOptions = new DefaultFilesOptions();
        defaultFilesOptions.DefaultFileNames.Clear();
        defaultFilesOptions.DefaultFileNames.Add("index.html");

        app.UseDefaultFiles(defaultFilesOptions);

        Log.Debug("Default files configured (index.html)");
        return app;
    }

    /// <summary>Serves static files with per-extension cache durations from ContentTypeHelper</summary>
    public static IApplicationBuilder UseStaticFilesWithCaching(this IApplicationBuilder app)
    {
        var staticFileOptions = new StaticFileOptions
        {
            OnPrepareResponse = context =>
            {
                var path = context.Context.Request.Path.Value ?? "";
                var extension = Path.GetExtension(path);

                var cacheDuration = ContentTypeHelper.GetCacheDuration(extension);
                context.Context.Response.Headers.CacheControl = $"public, max-age={(int)cacheDuration.TotalSeconds}";
                context.Context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            }
        };

        app.UseStaticFiles(staticFileOptions);
        Log.Debug("Static files with caching configured");
        return app;
    }
}
