using System.Net;
using PortwayApi.Classes;
using PortwayApi.Tests.Base;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

/// <summary>
/// Proxy cache keys include records
/// </summary>
public sealed class ProxyCacheKeyTests : ApiTestBase, IDisposable
{
    private const string Env = "500";
    private const int UpstreamPort = 8027;
    private static readonly string Dir = Path.Combine("endpoints", "Proxy", "CacheKeyTest");
    private readonly UpstreamCapture _upstream = new(UpstreamPort);

    public ProxyCacheKeyTests()
    {
        SetAllowedEnvironments(Env);
        Directory.CreateDirectory(Path.Combine(Dir, "Accounts"));
        File.WriteAllText(Path.Combine(Dir, "Accounts", "entity.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            Url = $"http://localhost:{UpstreamPort}/accounts",
            Methods = new[] { "GET", "POST", "PUT", "PATCH", "DELETE" },
            AllowedEnvironments = new[] { Env }
        }));
        EndpointHandler.ReloadAllEndpoints();
    }

    public new void Dispose()
    {
        Directory.Delete(Dir, recursive: true);
        EndpointHandler.ReloadAllEndpoints();
        _upstream.Dispose();
        base.Dispose();
    }

    // the startup health check requests the bare url
    private int Hits => _upstream.Requests.Count(r => r.Contains("$top="));

    private async Task Get(string suffix)
    {
        var response = await _client.GetAsync($"/api/{Env}/CacheKeyTest/Accounts{suffix}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DistinctRecordKeys_ReachUpstreamSeparately()
    {
        await Get("('KEYC3')?$top=8");
        await Get("('KEYC3')?$top=8");
        Assert.Equal(1, Hits);

        await Get("('KEYD4')?$top=8");

        Assert.Equal(2, Hits);
        Assert.Contains(_upstream.Requests, r => r.StartsWith("/accounts/KEYD4"));
    }

    [Fact]
    public async Task RecordKey_DoesNotShareTheCollectionEntry()
    {
        await Get("?$top=50");
        await Get("('ZZ9')?$top=50");

        Assert.Equal(2, Hits);
        Assert.Contains(_upstream.Requests, r => r.StartsWith("/accounts/ZZ9"));
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Write_InvalidatesCachedReads(string method)
    {
        await Get("?$top=9");
        await Get("?$top=9");
        Assert.Equal(1, Hits);

        using var write = new HttpRequestMessage(new HttpMethod(method), $"/api/{Env}/CacheKeyTest/Accounts('W1')");
        if (method != "DELETE")
            write.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        var response = await _client.SendAsync(write, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);

        await Get("?$top=9");
        Assert.Equal(2, Hits);
    }

    [Fact]
    public async Task AuthenticatedRead_RequiresRevalidation()
    {
        var response = await _client.GetAsync($"/api/{Env}/CacheKeyTest/Accounts?$top=7", TestContext.Current.CancellationToken);

        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoCache);
        Assert.Null(response.Headers.CacheControl?.MaxAge);
        Assert.Contains("Authorization", response.Headers.Vary);
    }
}
