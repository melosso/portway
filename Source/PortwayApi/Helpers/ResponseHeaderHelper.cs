namespace PortwayApi.Helpers;

/// <summary>Writes pagination and cache-control response headers outside ControllerBase</summary>
public static class ResponseHeaderHelper
{
    public static void SetPaginationHeaders(HttpContext context, int? totalCount, int returnedCount, bool hasMore = false)
    {
        if (totalCount.HasValue)
        {
            context.Response.Headers["X-Total-Count"] = totalCount.Value.ToString();
        }
        context.Response.Headers["X-Returned-Count"] = returnedCount.ToString();
        context.Response.Headers["X-Has-More"] = hasMore.ToString().ToLowerInvariant();
    }

    public static void SetCacheControlHeader(HttpContext context, int maxAgeSeconds = 300, bool isPublic = true)
    {
        var cacheType = isPublic ? "public" : "private";
        context.Response.Headers["Cache-Control"] = $"{cacheType}, max-age={maxAgeSeconds}";
    }
}
