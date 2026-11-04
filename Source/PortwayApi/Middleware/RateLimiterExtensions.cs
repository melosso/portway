namespace PortwayApi.Middleware;

using Microsoft.AspNetCore.Builder;

// Extension methods for adding RateLimiter middleware
public static class RateLimiterExtensions
{
    public static IApplicationBuilder UseRateLimiter(this IApplicationBuilder builder, bool webUiEnabled)
    {
        return builder.UseMiddleware<RateLimiter>(webUiEnabled);
    }

    /// <summary>
    /// Logs the rate limit settings, with the other feature lines after startup loading
    /// </summary>
    public static void LogRateLimitStatus(this IApplicationBuilder app)
    {
        var settings = app.ApplicationServices.GetRequiredService<RateLimitSettings>();
        if (!settings.Enabled)
        {
            Serilog.Log.Information("Rate limiting: Disabled");
            return;
        }

        var store = app.ApplicationServices.GetRequiredService<IRateLimiterStore>() is RedisRateLimiterStore ? "Redis" : "Memory";
        Serilog.Log.Information("Rate limiting: {Store}, {IpLimit} per {IpWindow}s per IP, {TokenLimit} per {TokenWindow}s per token",
            store, settings.IpLimit, settings.IpWindowSeconds, settings.TokenLimit, settings.TokenWindowSeconds);
    }
}
