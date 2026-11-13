using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Moq;
using PortwayApi.Classes;
using PortwayApi.Tests.Base;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

public sealed class ProxySecretTests : ApiTestBase, IDisposable
{
    private const string Env = "500";
    private const int UpstreamPort = 8041;
    private const string Secret = "erp-secret+&=value";
    private static readonly string Dir = Path.Combine("endpoints", "Proxy", "SecretTest");
    private readonly UpstreamCapture _upstream = new(UpstreamPort);

    public ProxySecretTests()
    {
        SetAllowedEnvironments(Env);
        Write("Header", new
        {
            Url = $"http://localhost:{UpstreamPort}/header",
            Methods = new[] { "GET", "POST" },
            AllowedEnvironments = new[] { Env },
            Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer ${secret:ERP-KEY}", ["X-Api-Key"] = "${secret:ERP-KEY}" }
        });
        Write("Query", new { Url = $"http://localhost:{UpstreamPort}/query?key=${{secret:ERP-KEY}}", Methods = new[] { "GET" }, AllowedEnvironments = new[] { Env } });
        Write("Missing", new { Url = $"http://localhost:{UpstreamPort}/missing?key=${{secret:NOPE}}", Methods = new[] { "GET" }, AllowedEnvironments = new[] { Env } });
        Write("Flow", new
        {
            Type = "Composite",
            Url = $"http://localhost:{UpstreamPort}/flow",
            Methods = new[] { "POST" },
            AllowedEnvironments = new[] { Env },
            CompositeConfig = new
            {
                Name = "Flow",
                Steps = new[]
                {
                    new { Name = "Only", Endpoint = "SecretTest/Header", Method = "POST", TemplateTransformations = new Dictionary<string, string> { ["apiKey"] = "${secret:ERP-KEY}" } }
                }
            },
        });
        EndpointHandler.ReloadAllEndpoints();
        _mockEnvironmentSettingsProvider.Setup(p => p.GetSecretAsync(Env, "ERP-KEY", It.IsAny<CancellationToken>())).ReturnsAsync(Secret);
        AddAuthorizationHeader();
    }

    public new void Dispose()
    {
        Directory.Delete(Dir, recursive: true);
        EndpointHandler.ReloadAllEndpoints();
        _upstream.Dispose();
        base.Dispose();
    }

    private static void Write(string name, object content)
    {
        Directory.CreateDirectory(Path.Combine(Dir, name));
        File.WriteAllText(Path.Combine(Dir, name, "entity.json"), JsonSerializer.Serialize(content));
    }

    private Task<HttpResponseMessage> Get(string path) => _client.GetAsync($"/api/{Env}/SecretTest/{path}", TestContext.Current.CancellationToken);

    private Task<HttpResponseMessage> PostFlow(string body) =>
        _client.PostAsync($"/api/{Env}/SecretTest/Flow", new StringContent(body, Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);

    [Fact]
    public async Task EntityAuthorization_ReplacesClientToken()
    {
        var response = await Get("Header");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal($"Bearer {Secret}", _upstream.Headers["Authorization"]);
        Assert.Equal(Secret, _upstream.Headers["X-Api-Key"]);
    }

    [Fact]
    public async Task QueryReference_ArrivesEscapedAndIntact()
    {
        var response = await Get("Query");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal($"/query?key={Uri.EscapeDataString(Secret)}", _upstream.PathAndQuery);
    }

    [Fact]
    public async Task ClientQuery_SecretReference_IsNotResolved()
    {
        await Get("Header?q=${secret:ERP-KEY}");

        Assert.DoesNotContain("erp-secret", _upstream.PathAndQuery);
        Assert.Contains("secret:ERP-KEY", Uri.UnescapeDataString(_upstream.PathAndQuery!));
    }

    [Fact]
    public async Task RawClientQuery_SecretReference_IsNotResolved()
    {
        await _factory.Server.SendAsync(ctx =>
        {
            ctx.Request.Method = "GET";
            ctx.Request.Path = $"/api/{Env}/SecretTest/Header";
            ctx.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString("?q=${secret:ERP-KEY}");
            ctx.Request.Headers.Authorization = "Bearer test-token";
        }, TestContext.Current.CancellationToken);

        Assert.NotNull(_upstream.PathAndQuery);
        Assert.DoesNotContain("erp-secret", _upstream.PathAndQuery);
        Assert.Contains("secret:ERP-KEY", Uri.UnescapeDataString(_upstream.PathAndQuery));
    }

    [Fact]
    public async Task ClientQuery_CollidingWithReference_IsDropped()
    {
        await Get("Query?key=attacker&other=1");

        Assert.DoesNotContain("attacker", _upstream.PathAndQuery);
        Assert.Contains("other=1", _upstream.PathAndQuery);
    }

    [Fact]
    public async Task MissingUrlSecret_FailsClosed()
    {
        var response = await Get("Missing");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Null(_upstream.PathAndQuery);
    }

    [Fact]
    public async Task ResolvedSecret_AbsentFromTrafficTarget()
    {
        var response = await Get("Query");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("erp-secret", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CompositeStep_SendsEntityHeadersAndTemplateSecret()
    {
        var response = await PostFlow("{\"a\":1}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Secret, _upstream.Headers["X-Api-Key"]);
        Assert.Equal(Secret, JsonDocument.Parse(_upstream.Body!).RootElement.GetProperty("apiKey").GetString());
    }

    [Fact]
    public async Task CompositeClientBody_SecretReference_IsNotResolved()
    {
        await PostFlow("{\"note\":\"${secret:ERP-KEY}\"}");

        Assert.Equal("${secret:ERP-KEY}", JsonDocument.Parse(_upstream.Body!).RootElement.GetProperty("note").GetString());
    }

    [Fact]
    public async Task CompositeEcho_SecretMaskedInResponse()
    {
        _upstream.ResponseBody = """{"echo":"{header:X-Api-Key}"}""";

        var body = await (await PostFlow("{\"a\":1}")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("erp-secret", body);
        Assert.Contains("***", body);
    }

    [Theory]
    [InlineData("""{"error":"bad key {header:X-Api-Key}"}""")]
    [InlineData("plain text with {header:X-Api-Key} echoed")]
    public async Task CompositeStepFailure_OmitsUpstreamBody(string upstreamBody)
    {
        _upstream.ResponseStatus = 400;
        _upstream.ResponseBody = upstreamBody;

        var response = await PostFlow("{\"a\":1}");
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var root = JsonDocument.Parse(body).RootElement;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("erp-secret", body);
        Assert.DoesNotContain(Uri.EscapeDataString(Secret), body);
        Assert.DoesNotContain("bad key", body);
        Assert.DoesNotContain("echoed", body);
        Assert.Equal("Upstream returned HTTP 400", root.GetProperty("details").GetString());
        Assert.Equal("Only", root.GetProperty("step").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
    }
}
