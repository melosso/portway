namespace PortwayApi.Middleware;

using Microsoft.AspNetCore.Builder;

// Extension methods for adding RateLimiter middleware
public static class RateLimiterExtensions
{
    public static IApplicationBuilder UseRateLimiter(this IApplicationBuilder builder, bool webUiEnabled)
    {
        return builder.UseMiddleware<RateLimiter>(webUiEnabled);
    }
}
