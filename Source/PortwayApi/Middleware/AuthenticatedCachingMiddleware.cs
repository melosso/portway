// Create a new class file named AuthenticatedCachingMiddleware.cs
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace PortwayApi.Middleware
{
    public class AuthenticatedCachingMiddleware
    {
        private readonly RequestDelegate _next;

        public AuthenticatedCachingMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Check if the user is authenticated
            bool isAuthenticated = !string.IsNullOrEmpty(context.User?.Identity?.Name) || 
                                  context.Request.Headers.ContainsKey("Authorization");

            if (isAuthenticated)
            {
                // Set the response cache header for authenticated users
                context.Response.GetTypedHeaders().CacheControl = 
                    new Microsoft.Net.Http.Headers.CacheControlHeaderValue
                    {
                        Public = false,
                        Private = true,
                        MaxAge = TimeSpan.FromMinutes(10)
                    };
                
                // Add Vary by Authorization to ensure different users get different cache entries
                context.Response.Headers.Append("Vary", "Authorization");
            }
            else
            {
                // For anonymous users, disable caching
                context.Response.GetTypedHeaders().CacheControl = 
                    new Microsoft.Net.Http.Headers.CacheControlHeaderValue
                    {
                        NoStore = true,
                        NoCache = true
                    };
            }

            await _next(context);
        }
    }

    // Extension method
    public static class AuthenticatedCachingMiddlewareExtensions
    {
        public static IApplicationBuilder UseAuthenticatedCaching(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<AuthenticatedCachingMiddleware>();
        }
    }
}