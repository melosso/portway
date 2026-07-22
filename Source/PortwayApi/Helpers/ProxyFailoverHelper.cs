using PortwayApi.Classes;
using Serilog;

namespace PortwayApi.Helpers;

/// <summary>Builds candidate upstream URLs and classifies transient failures for proxy failover</summary>
public static class ProxyFailoverHelper
{
    /// <summary>Sends a request with retry per URL and failover across fallbacks; single attempt when not configured</summary>
    public static async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpClient client,
        Func<string, HttpRequestMessage> buildRequest,
        string fullUrl,
        string baseUrl,
        List<string>? fallbackUrls,
        ProxyRetryOptions? retry,
        string logContext,
        CancellationToken ct)
    {
        int attemptsPerUrl = Math.Max(1, retry?.Attempts ?? 1);
        int retryDelayMs = Math.Max(0, retry?.DelayMs ?? 200);
        var candidateUrls = BuildCandidateUrls(fullUrl, baseUrl, fallbackUrls);
        bool failoverEnabled = candidateUrls.Count > 1 || attemptsPerUrl > 1;

        for (int urlIndex = 0; urlIndex < candidateUrls.Count; urlIndex++)
        {
            for (int attempt = 1; attempt <= attemptsPerUrl; attempt++)
            {
                var targetUrl = candidateUrls[urlIndex];
                bool lastTry = urlIndex == candidateUrls.Count - 1 && attempt == attemptsPerUrl;

                try
                {
                    var response = await client.SendAsync(buildRequest(targetUrl), ct);

                    if (!failoverEnabled || lastTry || !IsTransientStatus((int)response.StatusCode))
                        return response;

                    Log.Warning("Proxy attempt {Attempt} to {Url} for {Context} returned {StatusCode}, trying next",
                        attempt, targetUrl, logContext, (int)response.StatusCode);
                    response.Dispose();
                }
                catch (Exception ex) when (failoverEnabled && !lastTry &&
                    ex is HttpRequestException or TaskCanceledException &&
                    !ct.IsCancellationRequested)
                {
                    Log.Warning("Proxy attempt {Attempt} to {Url} for {Context} failed: {Error}, trying next",
                        attempt, targetUrl, logContext, ex.Message);
                }

                if (retryDelayMs > 0)
                    await Task.Delay(retryDelayMs, ct);
            }
        }

        throw new HttpRequestException($"All proxy attempts failed for {logContext}");
    }

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
