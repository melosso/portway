namespace PortwayApi.Helpers;

/// <summary>
/// Writes pagination response headers outside ControllerBase
/// </summary>
public static class HttpResponseHeaderHelper
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
}
