namespace PortwayApi.Endpoints;

using Microsoft.AspNetCore.Builder;
using PortwayApi.Services;

/// <summary>
/// Extension methods for mapping health check endpoints
/// </summary>
public static class HealthCheckEndpointExtensions
{
    public static WebApplication MapHealthCheckEndpoints(this WebApplication app)
    {
        app.MapGet("/health", async (HttpContext context, 
                                     HealthCheckService healthService) =>
        {
            // Get cached health report
            var report = await healthService.CheckHealthAsync();
            
            // Add cache headers
            context.Response.Headers.CacheControl = "public, max-age=15";
            context.Response.Headers.Append("Expires", DateTime.UtcNow.AddSeconds(15).ToString("R"));
            
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = report.Status == Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy 
                ? StatusCodes.Status200OK 
                : StatusCodes.Status503ServiceUnavailable;
                
            await context.Response.WriteAsJsonAsync(new 
            { 
                status = report.Status.ToString(),
                timestamp = DateTime.UtcNow,
                cache_expires_in = "15 seconds" 
            });
        })
        .ExcludeFromDescription();

        app.MapGet("/health/live", async (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "public, max-age=5";
            context.Response.Headers.Append("Expires", DateTime.UtcNow.AddSeconds(5).ToString("R"));
            
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync("Alive");
        })
        .ExcludeFromDescription();

        app.MapGet("/health/details", async (HttpContext context, 
                                          HealthCheckService healthService) =>
        {
            // Get cached health report
            var report = await healthService.CheckHealthAsync();
            
            // Add cache headers
            context.Response.Headers.CacheControl = "public, max-age=60";
            context.Response.Headers.Append("Expires", DateTime.UtcNow.AddSeconds(60).ToString("R"));
            
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = report.Status switch
            {
                Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy => StatusCodes.Status200OK,
                Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded => StatusCodes.Status200OK,
                Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy => StatusCodes.Status503ServiceUnavailable,
                _ => StatusCodes.Status500InternalServerError
            };
            
            var result = new
            {
                status = report.Status.ToString(),
                timestamp = DateTime.UtcNow,
                cache_expires_in = "60 seconds",
                checks = report.Entries.Select(e => new
                {
                    name = e.Key,
                    status = e.Value.Status.ToString(),
                    description = e.Value.Description,
                    duration = $"{e.Value.Duration.TotalMilliseconds:F2}ms",
                    data = e.Value.Data.Count > 0 ? e.Value.Data : null,
                    tags = e.Value.Tags
                }),
                totalDuration = $"{report.TotalDuration.TotalMilliseconds:F2}ms",
                version = typeof(HealthCheckEndpointExtensions).Assembly.GetName().Version?.ToString() ?? "Unknown"
            };
            
            await context.Response.WriteAsJsonAsync(result);
        })
        .ExcludeFromDescription();

        return app;
    }
}