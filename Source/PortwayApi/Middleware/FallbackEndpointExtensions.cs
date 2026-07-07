namespace PortwayApi.Middleware;

using Serilog;

/// <summary>Fallback for unmatched routes; HTML 404 for browsers, JSON for API clients</summary>
public static class FallbackEndpointExtensions
{
    public static WebApplication MapPortwayFallback(this WebApplication app)
    {
        app.MapFallback(async context =>
        {
            var path = context.Request.Path.Value;
            Log.Warning("Unmatched route: {Method} {Path}", context.Request.Method, path);

            context.Response.StatusCode = 404;

            var acceptHeader = context.Request.Headers.Accept.ToString();
            var isBrowserRequest = acceptHeader.Contains("text/html");

            if (isBrowserRequest)
            {
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.WriteAsync("""
                    <!DOCTYPE html>
                    <html lang="en">
                    <head>
                      <meta charset="utf-8">
                      <meta name="viewport" content="width=device-width, initial-scale=1">
                      <title>404 Not Found · Portway</title>
                      <link href="/css/site.css" rel="stylesheet">
                    </head>
                    <body style="display:flex;align-items:center;justify-content:center;height:100vh">
                      <div style="text-align:center;max-width:420px;padding:2rem">
                        <div style="font-size:3rem;font-weight:700;letter-spacing:-0.04em;color:hsl(var(--muted-foreground))">404</div>
                        <div style="font-size:1rem;font-weight:600;margin:0.75rem 0 0.5rem">Page not found</div>
                        <div style="font-size:0.875rem;color:hsl(var(--muted-foreground));margin-bottom:1.5rem">
                          The page you're looking for doesn't exist or has been moved.
                        </div>
                        <a href="/ui/dashboard" style="display:inline-flex;align-items:center;gap:0.375rem;padding:0.5rem 1rem;background:hsl(var(--primary));color:hsl(var(--primary-foreground));border-radius:var(--radius);text-decoration:none;font-size:0.875rem;font-weight:500">
                          Go to Dashboard
                        </a>
                      </div>
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
        });

        return app;
    }
}
