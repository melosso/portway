using System.Net;
using System.Text;
using Moq;
using PortwayApi.Tests.Base;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

/// <summary>Captures what the proxy actually sends upstream, one request at a time</summary>
internal sealed class UpstreamCapture : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();

    public string? Method { get; private set; }
    public string? PathAndQuery { get; private set; }
    public string? Body { get; private set; }
    public string? ContentType { get; private set; }
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Extra response headers for the next reply, e.g. Set-Cookie</summary>
    public Dictionary<string, string> ResponseHeaders { get; } = new();

    public UpstreamCapture(int port)
    {
        _listener.Prefixes.Add($"http://localhost:{port}/");
        _listener.Start();
        _ = Task.Run(ListenLoop);
    }

    private async Task ListenLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch (Exception) { return; } // listener stopped

            Method       = ctx.Request.HttpMethod;
            PathAndQuery = ctx.Request.Url?.PathAndQuery;
            ContentType  = ctx.Request.ContentType;
            Headers.Clear();
            foreach (var key in ctx.Request.Headers.AllKeys)
                if (key != null)
                    Headers[key] = ctx.Request.Headers[key] ?? "";

            using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                Body = await reader.ReadToEndAsync();

            foreach (var (k, v) in ResponseHeaders)
                ctx.Response.Headers.Add(k, v);

            var payload = Encoding.UTF8.GetBytes("""{"ok":true}""");
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.OutputStream.WriteAsync(payload);
            ctx.Response.Close();
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); _listener.Close(); } catch { }
    }
}

/// <summary>
/// Protocol compatibility for proxy passthrough (JSON-RPC, XML, GraphQL, headers, cookies) via the demo endpoint targeting localhost:8020
/// </summary>
public class ProxyProtocolCompatibilityTests : ApiTestBase, IDisposable
{
    private const string ApiPath = "/api/500/Account/Accounts";
    private readonly UpstreamCapture _upstream;

    public ProxyProtocolCompatibilityTests()
    {
        SetAllowedEnvironments("500", "700");
        _upstream = new UpstreamCapture(8020);
    }

    private void SetEnvironmentHeaders(Dictionary<string, string> headers)
    {
        _mockEnvironmentSettingsProvider
            .Setup(p => p.LoadEnvironmentOrThrowAsync(It.IsAny<string>()))
            .ReturnsAsync(("Server=localhost;Database=test;Trusted_Connection=True", "localhost", headers));
    }

    // Body passthrough

    [Fact]
    public async Task JsonRpcBody_ReachesUpstreamVerbatim()
    {
        var envelope = """{"jsonrpc":"2.0","method":"call","params":{"service":"common","method":"authenticate","args":["db","user","key",{}]}}""";

        var response = await _client.PostAsync(ApiPath,
            new StringContent(envelope, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("POST", _upstream.Method);
        Assert.Equal(envelope, _upstream.Body);
        Assert.Contains("application/json", _upstream.ContentType);
    }

    [Fact]
    public async Task GraphQlStylePost_QueryBodyReachesUpstreamVerbatim()
    {
        var query = """{"query":"{ getProductListing(first: 25) { edges { node { id name } } } }"}""";

        var response = await _client.PostAsync(ApiPath,
            new StringContent(query, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(query, _upstream.Body);
    }

    [Fact]
    public async Task XmlBody_ReachesUpstreamWithContentTypePreserved()
    {
        var xml = """<?xml version="1.0"?><methodCall><methodName>execute</methodName></methodCall>""";

        var response = await _client.PostAsync(ApiPath,
            new StringContent(xml, Encoding.UTF8, "application/xml"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(xml, _upstream.Body);
        Assert.Contains("application/xml", _upstream.ContentType);
    }

    // Query string passthrough

    [Fact]
    public async Task QueryParameters_PassThroughUntouched()
    {
        var response = await _client.GetAsync(ApiPath + "?filterfieldids=ItemCode&filtervalues=A0001&take=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("filterfieldids=ItemCode", _upstream.PathAndQuery);
        Assert.Contains("filtervalues=A0001", _upstream.PathAndQuery);
        Assert.Contains("take=100", _upstream.PathAndQuery);
    }

    // Environment header injection

    [Fact]
    public async Task EnvironmentHeaders_AreInjectedUpstream()
    {
        SetEnvironmentHeaders(new() { ["xc-token"] = "nocodb-secret-token" });

        var response = await _client.GetAsync(ApiPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nocodb-secret-token", _upstream.Headers["xc-token"]);
    }

    // Authorization collision (basis of the Teable/AFAS auth-swap guidance)

    [Fact]
    public async Task ClientAuthorization_IsForwardedUpstream()
    {
        var response = await _client.GetAsync(ApiPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Bearer test-token", _upstream.Headers["Authorization"]);
    }

    [Fact]
    public async Task EnvironmentAuthorizationHeader_CollidesWithClientBearer()
    {
        // Pins the documented collision: client bearer and environment value both reach upstream as a multi-valued header
        SetEnvironmentHeaders(new() { ["Authorization"] = "Bearer upstream_token" });

        var response = await _client.GetAsync(ApiPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var auth = _upstream.Headers["Authorization"];
        Assert.Contains("test-token", auth);
        Assert.Contains("upstream_token", auth);
    }

    // Cookie passthrough (basis of the SAP B1 session pattern)

    [Fact]
    public async Task ClientCookies_ReachUpstream()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiPath);
        request.Headers.Add("Cookie", "B1SESSION=abc123; ROUTEID=.node1");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("B1SESSION=abc123", _upstream.Headers["Cookie"]);
    }

    [Fact]
    public async Task UpstreamSetCookie_ReturnsToClient()
    {
        _upstream.ResponseHeaders["Set-Cookie"] = "B1SESSION=xyz789; Path=/; HttpOnly";

        var response = await _client.GetAsync(ApiPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(response.Headers, h =>
            h.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase) &&
            h.Value.Any(v => v.Contains("B1SESSION=xyz789")));
    }

    // Method translation (basis of the QUERY read-only GraphQL recipe)

    [Fact]
    public async Task PutMethod_TranslatesToMergeUpstream()
    {
        // Demo endpoint config: HttpMethodTranslation "PUT:MERGE" plus X-Custom-Original-Method append header
        var response = await _client.PutAsync(ApiPath + "(guid'11111111-1111-1111-1111-111111111111')",
            new StringContent("""{"Name":"Updated"}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("MERGE", _upstream.Method);
        Assert.Equal("PUT", _upstream.Headers["X-Custom-Original-Method"]);
    }

    public new void Dispose()
    {
        _upstream.Dispose();
        base.Dispose();
    }
}

/// <summary>
/// Content negotiation gates by endpoint type: proxy-family paths skip the JSON-only check, SQL paths keep it
/// </summary>
public class ContentNegotiationByEndpointTypeTests : ApiTestBase
{
    public ContentNegotiationByEndpointTypeTests()
    {
        SetAllowedEnvironments("500", "700");
    }

    [Fact]
    public async Task XmlPost_ToSqlEndpoint_Returns415()
    {
        var response = await _client.PostAsync("/api/700/Product/Products",
            new StringContent("<item/>", System.Text.Encoding.UTF8, "application/xml"));

        Assert.Equal(System.Net.HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task JsonPost_ToSqlEndpoint_PassesContentGate()
    {
        var response = await _client.PostAsync("/api/700/Product/Products",
            new StringContent("""{"x":1}""", System.Text.Encoding.UTF8, "application/json"));

        // Downstream SQL failure is fine; the content gate specifically is what must not fire
        Assert.NotEqual(System.Net.HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }
}

/// <summary>
/// Pins URL construction for endpoint Urls carrying a baked query: paths join the path, queries merge with '&'
/// </summary>
public class QueryBearingUrlEndpointTests : ApiTestBase, IDisposable
{
    private const string EndpointDir = "endpoints/Proxy/QueryUrlTest";
    private readonly UpstreamCapture _upstream;

    public QueryBearingUrlEndpointTests()
    {
        SetAllowedEnvironments("500", "700");
        _upstream = new UpstreamCapture(8020);

        // Temporary endpoint fixture with a query baked into the Url; removed in Dispose
        Directory.CreateDirectory(EndpointDir);
        File.WriteAllText(Path.Combine(EndpointDir, "entity.json"), """
            {
              "Url": "http://localhost:8020/graphql?apikey=SECRET",
              "Methods": ["GET", "POST"]
            }
            """);
        PortwayApi.Classes.EndpointHandler.ReloadAllEndpoints();
    }

    [Fact]
    public async Task BakedQuery_TravelsWithPlainPost()
    {
        var response = await _client.PostAsync("/api/500/QueryUrlTest",
            new StringContent("""{"query":"{ ping }"}""", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/graphql?apikey=SECRET", _upstream.PathAndQuery);
    }

    [Fact]
    public async Task ClientQuery_MergesAfterBakedQuery()
    {
        var response = await _client.GetAsync("/api/500/QueryUrlTest?x=1");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/graphql?apikey=SECRET&x=1", _upstream.PathAndQuery);
    }

    [Fact]
    public async Task ClientParameter_CannotOverrideBakedParameter()
    {
        var response = await _client.GetAsync("/api/500/QueryUrlTest?apikey=evil&x=1");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/graphql?apikey=SECRET&x=1", _upstream.PathAndQuery);
    }

    [Fact]
    public async Task RemainingPath_AppendsToPathNotQuery()
    {
        var response = await _client.GetAsync("/api/500/QueryUrlTest/sub/path");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/graphql/sub/path?apikey=SECRET", _upstream.PathAndQuery);
    }

    public new void Dispose()
    {
        _upstream.Dispose();
        try { Directory.Delete(EndpointDir, recursive: true); } catch { }
        PortwayApi.Classes.EndpointHandler.ReloadAllEndpoints();
        base.Dispose();
    }
}
