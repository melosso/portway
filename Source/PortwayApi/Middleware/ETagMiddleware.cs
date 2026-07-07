using System.Security.Cryptography;

namespace PortwayApi.Middleware;

/// <summary>Strong ETags for GET /api responses; matching If-None-Match returns 304 without a body</summary>
public class ETagMiddleware
{
    private readonly RequestDelegate _next;

    public ETagMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method) ||
            !context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await _next(context);

            context.Response.Body = originalBody;
            buffer.Position = 0;

            if (context.Response.StatusCode == StatusCodes.Status200OK && buffer.Length > 0)
            {
                var etag = $"\"{Convert.ToHexStringLower(SHA256.HashData(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)))}\"";
                context.Response.Headers.ETag = etag;

                var ifNoneMatch = context.Request.Headers.IfNoneMatch;
                if (ifNoneMatch.Count > 0 &&
                    ifNoneMatch.Any(v => v != null && (v == etag || v == "*" || v.Split(',').Select(x => x.Trim()).Contains(etag))))
                {
                    context.Response.StatusCode = StatusCodes.Status304NotModified;
                    context.Response.ContentLength = 0;
                    return;
                }
            }

            if (buffer.Length > 0)
                await buffer.CopyToAsync(originalBody, context.RequestAborted);
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }
}

public static class ETagMiddlewareExtensions
{
    public static IApplicationBuilder UseETagCaching(this IApplicationBuilder builder)
        => builder.UseMiddleware<ETagMiddleware>();
}
