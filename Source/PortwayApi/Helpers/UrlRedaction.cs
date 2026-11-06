namespace PortwayApi.Helpers;

/// <summary>
/// Removes query values from a URL before it reaches a log or a console response. Endpoint URLs can hold credentials
/// </summary>
public static class UrlRedaction
{
    public static string Redact(string? url)
    {
        if (string.IsNullOrEmpty(url) || url.IndexOf('?') is not (>= 0 and var start))
            return url ?? string.Empty;

        var pairs = url[(start + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.IndexOf('=') is >= 0 and var equals ? $"{pair[..equals]}=***" : pair);

        return $"{url[..start]}?{string.Join('&', pairs)}";
    }
}
