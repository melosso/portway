using PortwayApi.Classes;
using Serilog;

namespace PortwayApi.Helpers;

/// <summary>
/// Builds candidate upstream URLs and classifies transient failures for proxy failover
/// </summary>
public static class ProxyFailoverHelper
{
    /// <summary>
    /// Sends a request with retry per URL and failover across fallbacks; single attempt when not configured
    /// </summary>
    public static async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpClient client,
        Func<string, HttpRequestMessage> buildRequest,
        string fullUrl,
        string baseUrl,
        List<string>? fallbackUrls,
        ProxyRetryOptions? retry,
        string logContext,
        Func<string, bool> isUrlSafe,
        CancellationToken ct,
        bool sameHostRedirectsOnly = false)
    {
        int attemptsPerUrl = Math.Max(1, retry?.Attempts ?? 1);
        int retryDelayMs = Math.Max(0, retry?.DelayMs ?? 200);
        var candidateUrls = BuildCandidateUrls(fullUrl, baseUrl, fallbackUrls);
        bool failoverEnabled = candidateUrls.Count > 1 || attemptsPerUrl > 1;

        for (int urlIndex = 0; urlIndex < candidateUrls.Count; urlIndex++)
        {
            var targetUrl = candidateUrls[urlIndex];

            // Revalidate every candidate against the SSRF allow list; fallback URLs are not checked upstream
            if (!isUrlSafe(targetUrl))
            {
                Log.Warning("Proxy candidate {Url} for {Context} failed URL safety validation, skipping",
                    UrlRedaction.Redact(targetUrl), logContext);
                continue;
            }

            for (int attempt = 1; attempt <= attemptsPerUrl; attempt++)
            {
                bool lastTry = urlIndex == candidateUrls.Count - 1 && attempt == attemptsPerUrl;

                try
                {
                    var response = await client.SendAsync(buildRequest(targetUrl), ct);
                    if (sameHostRedirectsOnly)
                        response = await FollowSameHostRedirectsAsync(client, buildRequest, targetUrl, response, isUrlSafe, ct);

                    if (!failoverEnabled || lastTry || !IsTransientStatus((int)response.StatusCode))
                        return response;

                    Log.Warning("Proxy attempt {Attempt} to {Url} for {Context} returned {StatusCode}, trying next",
                        attempt, UrlRedaction.Redact(targetUrl), logContext, (int)response.StatusCode);
                    response.Dispose();
                }
                catch (Exception ex) when (failoverEnabled && !lastTry &&
                    ex is HttpRequestException or TaskCanceledException &&
                    !ct.IsCancellationRequested)
                {
                    Log.Warning("Proxy attempt {Attempt} to {Url} for {Context} failed: {Error}, trying next",
                        attempt, UrlRedaction.Redact(targetUrl), logContext, ex.Message);
                }

                if (retryDelayMs > 0)
                    await Task.Delay(retryDelayMs, ct);
            }
        }

        throw new HttpRequestException($"All proxy attempts failed for {logContext}");
    }

    public const int MaxRedirects = 5;

    public static string ClientName(bool sameHostRedirectsOnly) =>
        sameHostRedirectsOnly ? Middleware.ProxyHttpClientExtensions.NoRedirectClient : Middleware.ProxyHttpClientExtensions.Client;

    internal static async Task<HttpResponseMessage> FollowSameHostRedirectsAsync(
        HttpClient client,
        Func<string, HttpRequestMessage> buildRequest,
        string templateUrl,
        HttpResponseMessage response,
        Func<string, bool> isUrlSafe,
        CancellationToken ct)
    {
        var current = response.RequestMessage?.RequestUri ?? new Uri(templateUrl);
        var method = response.RequestMessage?.Method ?? HttpMethod.Get;
        var bodyDropped = false;
        for (var hop = 0; hop < MaxRedirects && IsRedirect((int)response.StatusCode) && response.Headers.Location is { } location; hop++)
        {
            var next = location.IsAbsoluteUri ? location : new Uri(current, location);
            if (!IsSameHostOrUpgrade(current, next) || !isUrlSafe(next.AbsoluteUri))
            {
                Log.Debug("Redirect to another host is returned to the client");
                return response;
            }

            if ((int)response.StatusCode == 303 || ((int)response.StatusCode is 301 or 302 && method == HttpMethod.Post))
            {
                method = method == HttpMethod.Head ? HttpMethod.Head : HttpMethod.Get;
                bodyDropped = true;
            }

            var request = buildRequest(templateUrl);
            request.RequestUri = next;
            request.Method = method;
            if (bodyDropped)
                request.Content = null;

            current = next;
            response.Dispose();
            response = await client.SendAsync(request, ct);
        }
        return response;
    }

    internal static bool IsRedirect(int status) => status is 301 or 302 or 303 or 307 or 308;

    internal static bool IsSameHostOrUpgrade(Uri from, Uri to) =>
        from.Host.Equals(to.Host, StringComparison.OrdinalIgnoreCase)
        && ((from.Scheme == to.Scheme && from.Port == to.Port)
            || (from.Scheme == Uri.UriSchemeHttp && to.Scheme == Uri.UriSchemeHttps && (to.IsDefaultPort || to.Port == from.Port)));

    public static List<string> BuildCandidateUrls(string fullUrl, string baseUrl, List<string>? fallbackUrls)
    {
        var candidates = new List<string> { fullUrl };
        if (fallbackUrls == null || fallbackUrls.Count == 0)
            return candidates;

        var basePrefix = baseUrl.TrimEnd('/');
        if (string.IsNullOrEmpty(basePrefix) || !fullUrl.StartsWith(basePrefix, StringComparison.OrdinalIgnoreCase))
            return candidates;

        var suffix = fullUrl[basePrefix.Length..];
        foreach (var fallback in fallbackUrls)
        {
            if (!string.IsNullOrWhiteSpace(fallback))
                candidates.Add(fallback.TrimEnd('/') + suffix);
        }

        return candidates;
    }

    public static bool IsTransientStatus(int statusCode) => statusCode is 502 or 503 or 504;
}
