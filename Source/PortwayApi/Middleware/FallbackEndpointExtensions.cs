namespace PortwayApi.Middleware;

using Serilog;

/// <summary>
/// Fallback for unmatched routes; HTML 404 for browsers, JSON for API clients
/// </summary>
public static class FallbackEndpointExtensions
{
    public static WebApplication MapPortwayFallback(this WebApplication app)
    {
        app.MapFallback(WriteNotFoundAsync);

        return app;
    }

    public static async Task WriteNotFoundAsync(HttpContext context)
    {
        var path = context.Request.Path.Value;
        Log.Warning("Unmatched route: {Method} {Path}", context.Request.Method, path);

        context.Response.StatusCode = 404;

        var acceptHeader = context.Request.Headers.Accept.ToString();
        var isBrowserRequest = acceptHeader.Contains("text/html");

        if (isBrowserRequest)
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            // PathBase prefixes asset and link URLs when the gateway runs under a route prefix
            var pathBase = System.Net.WebUtility.HtmlEncode(context.Request.PathBase.Value ?? "");
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            await context.Response.WriteAsync($$"""
                <!DOCTYPE html>
                <html lang="en">
                <head>
                  <meta charset="utf-8">
                  <meta name="viewport" content="width=device-width, initial-scale=1">
                  <meta name="robots" content="noindex,nofollow">
                  <title>404 Not Found · Portway</title>
                  <link href="{{pathBase}}/css/site.css" rel="stylesheet">
                  <script src="{{pathBase}}/js/theme-toggle.js"></script>
                </head>
                <body>
                  <main class="status-page">
                    <section class="status-card">
                      <div class="status-head">
                        <span class="status-code">404</span>
                        <h1>Page not found</h1>
                      </div>
                      <p>No page exists at this address.</p>
                      <dl class="status-meta">
                        <div><dt>Code</dt><dd>NOT_FOUND</dd></div>
                        <div><dt>Timestamp</dt><dd>{{timestamp}} UTC</dd></div>
                      </dl>
                      <a href="{{pathBase}}/_/dashboard" class="btn btn-primary">Go to dashboard</a>
                    </section>
                  </main>
                </body>
                </html>
                """);
        }
        else
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Route not found",
                path = path,
                timestamp = DateTime.UtcNow
            });
        }
    }
}
