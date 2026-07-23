using System.Net;
using System.Net.Http.Json;
using Xunit;
using PortwayApi.Tests.Base;

namespace PortwayApi.Tests.Middleware;

// Pins interaction with .NET 11 automatic cross-origin CSRF marking (Sec-Fetch-Site / Origin based)
public class CsrfProtectionTests : ApiTestBase
{
    private async Task<HttpResponseMessage> PostAsync(string path, string? secFetchSite, string? origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(new { probe = true })
        };
        if (secFetchSite != null) request.Headers.Add("Sec-Fetch-Site", secFetchSite);
        if (origin != null) request.Headers.Add("Origin", origin);
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task DataPlanePost_ServerToServer_ReachesRouting()
    {
        var response = await PostAsync("/api/500/Nonexistent", null, null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DataPlanePost_SameOrigin_ReachesRouting()
    {
        var response = await PostAsync("/api/500/Nonexistent", "same-origin", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DataPlanePost_CrossSiteBrowser_NotRejectedByCsrfMarking()
    {
        // Bearer authenticated data plane must stay reachable for CORS based browser clients
        var response = await PostAsync("/api/500/Nonexistent", "cross-site", "https://spa.example");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DataPlanePost_ForeignOriginWithoutFetchMetadata_NotRejectedByCsrfMarking()
    {
        // Legacy browser path validates the Origin header instead of Sec-Fetch-Site
        var response = await PostAsync("/api/500/Nonexistent", null, "https://spa.example");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CrossSiteGet_SafeMethodAllowed()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("Sec-Fetch-Site", "cross-site");
        request.Headers.Add("Origin", "https://evil.example");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UiMutation_CrossSiteWithoutPortwayCsrf_Rejected()
    {
        // Portway's own double submit CSRF guards the cookie authenticated UI plane
        var response = await PostAsync("/ui/api/client-error", "cross-site", "https://evil.example");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
