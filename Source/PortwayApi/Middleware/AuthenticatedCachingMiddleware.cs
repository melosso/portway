using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace PortwayApi.Middleware
{
    public class AuthenticatedCachingMiddleware
    {
        /// <summary>
        /// HttpContext item holding request header names the response depends on, e.g. tenant headers
        /// </summary>
        public const string VaryHeadersItem = "Portway.VaryHeaders";

        private readonly RequestDelegate _next;

        public AuthenticatedCachingMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            bool isAuthenticated = !string.IsNullOrEmpty(context.User?.Identity?.Name) ||
                                  context.Request.Headers.ContainsKey("Authorization");

            if (isAuthenticated)
            {
                context.Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue
                {
                    Private = true,
                    MaxAge = TimeSpan.FromMinutes(10)
                };

                // handlers replace these headers with upstream or cached values, so they are enforced when the response starts
                context.Response.OnStarting(() =>
                {
                    EnforcePrivateCaching(context);
                    return Task.CompletedTask;
                });
            }
            else
            {
                context.Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue
                {
                    NoStore = true,
                    NoCache = true
                };
            }

            await _next(context);
        }

        private static void EnforcePrivateCaching(HttpContext context)
        {
            var headers = context.Response.GetTypedHeaders();
            if (headers.CacheControl is { Public: true } cacheControl)
            {
                cacheControl.Public = false;
                cacheControl.Private = true;
                headers.CacheControl = cacheControl;
            }

            var vary = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ordered = new List<string>();
            void Add(string name)
            {
                if (vary.Add(name))
                    ordered.Add(name);
            }

            foreach (var value in context.Response.Headers.Vary)
                foreach (var name in (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    Add(name);
            Add("Authorization");
            if (context.Items[VaryHeadersItem] is IEnumerable<string> extra)
                foreach (var name in extra)
                    Add(name);

            context.Response.Headers.Vary = new StringValues(string.Join(", ", ordered));
        }
    }

    public static class AuthenticatedCachingMiddlewareExtensions
    {
        public static IApplicationBuilder UseAuthenticatedCaching(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<AuthenticatedCachingMiddleware>();
        }
    }
}
