namespace PortwayApi.Helpers;

/// <summary>
/// Removes credentials and query values from a URL before it reaches a log or a console response
/// </summary>
public static class UrlRedaction
{
    public static string Redact(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return string.Empty;

        var query = url.IndexOf('?');
        var target = RedactUserInfo(query < 0 ? url : url[..query]);
        if (query < 0)
            return target;

        var pairs = url[(query + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.IndexOf('=') is >= 0 and var equals ? $"{pair[..equals]}=***" : pair);

        return $"{target}?{string.Join('&', pairs)}";
    }

    private static string RedactUserInfo(string url)
    {
        var scheme = url.IndexOf("://", StringComparison.Ordinal);
        if (scheme < 0)
            return url;

        var start = scheme + 3;
        var slash = url.IndexOf('/', start);
        var at = (slash < 0 ? url.AsSpan(start) : url.AsSpan(start, slash - start)).LastIndexOf('@');
        return at < 0 ? url : $"{url[..start]}***{url[(start + at)..]}";
    }
}
