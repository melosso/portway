using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using PortwayApi.Auth;
using PortwayApi.Services.Mcp;
using PortwayApi.Tests.Base;
using Xunit;

namespace PortwayApi.Tests.Services;

/// <summary>
/// Tool calls stay inside the caller's bounds and report failures as failures
/// </summary>
public class McpToolExecutionTests
{
    private const string Token = "caller-token";
    private const string BaseUrl = "http://localhost:5000";

    private sealed class CapturingHandler(HttpStatusCode status, string response, string contentType) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string? Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add((request, request.Content is null ? null : await request.Content.ReadAsStringAsync(ct)));
            return new HttpResponseMessage(status) { Content = new StringContent(response, Encoding.UTF8, contentType) };
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static (McpChatService Service, CapturingHandler Handler) Build(
        EndpointMcpInfo endpoint,
        string scopes = "*",
        HttpStatusCode status = HttpStatusCode.OK,
        string response = "{\"Value\":[]}",
        string contentType = "application/json",
        int maxToolResultChars = 12000)
    {
        var registry = new McpEndpointRegistry();
        registry.RegisterEndpoints([endpoint]);

        var tokens = new Mock<TokenService>((AuthDbContext)null!, (ITokenVerificationCache)null!);
        tokens.Setup(t => t.GetTokenDetailsByTokenAsync(Token)).ReturnsAsync(new AuthToken
        {
            Username = "caller",
            TokenHash = "h",
            TokenSalt = "s",
            AllowedScopes = scopes
        });

        var services = new ServiceCollection().AddSingleton(tokens.Object).BuildServiceProvider();
        var handler = new CapturingHandler(status, response, contentType);
        var options = Options.Create(new McpOptions { DefaultPageSize = 50, MaxPageSize = 200, MaxToolResultChars = maxToolResultChars });

        return (new McpChatService(registry, options, null!, new Factory(handler),
            services.GetRequiredService<IServiceScopeFactory>(), new TestEnvironmentSettings()), handler);
    }

    private static EndpointMcpInfo Endpoint(params string[] methods) => new()
    {
        Name = "Items",
        Methods = methods,
        AllowedEnvironments = ["500"]
    };

    private static Task<McpToolResult> Call(McpChatService service, object input, string? token = Token) =>
        service.ExecuteToolAsync("items", JsonSerializer.Serialize(input), "500", BaseUrl, token, CancellationToken.None);

    [Theory]
    [InlineData("../../metrics")]
    [InlineData("500/../../metrics")]
    [InlineData("nope")]
    public async Task CraftedEnvironment_ReturnsErrorAndSendsNothing(string environment)
    {
        var endpoint = Endpoint("GET") with { AllowedEnvironments = null };
        var (service, handler) = Build(endpoint);

        var result = await Call(service, new { environment });

        Assert.True(result.IsError);
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task RefusedEnvironment_IsNotReplaced_AndListsAllowed()
    {
        var (service, handler) = Build(Endpoint("GET"));

        var result = await Call(service, new { environment = "700" });

        Assert.True(result.IsError);
        Assert.Contains("500", result.Text);
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task OutOfScopeTool_AnswersLikeUnknownTool()
    {
        var (service, handler) = Build(Endpoint("GET"), scopes: "Other");

        var refused = await Call(service, new { environment = "700" });

        Assert.True(refused.IsError);
        Assert.Equal("Unknown tool: items", refused.Text);
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task MissingToken_ReturnsError()
    {
        var (service, handler) = Build(Endpoint("GET"));

        var result = await Call(service, new { environment = "500" }, token: null);

        Assert.True(result.IsError);
        Assert.Empty(handler.Calls);
    }

    [Theory]
    [InlineData("%24top=900", "$top=200")]
    [InlineData("$top=900", "$top=200")]
    [InlineData("$TOP=900", "$top=200")]
    [InlineData("%24%74%6F%70=900", "$top=200")]
    [InlineData("$top=%39%30%30", "$top=200")]
    [InlineData("$top=5&%24top=900", "$top=5")]
    [InlineData("$top=abc", "$top=50")]
    [InlineData("$top=-1", "$top=50")]
    [InlineData("$filter=A eq 1#", "$filter=A eq 1&$top=50")]
    [InlineData("$top=5", "$top=5")]
    [InlineData("", "$top=50")]
    public void EnforceTopLimit_HoldsForEveryEncoding(string query, string expected)
    {
        Assert.Equal(expected, McpChatService.EnforceTopLimit(query, 50, 200));
    }

    [Fact]
    public async Task EncodedTop_SendsOneClampedTop()
    {
        var (service, handler) = Build(Endpoint("GET"));

        await Call(service, new { environment = "500", query = "%24top=900" });

        Assert.Equal("?$top=200", handler.Calls.Single().Request.RequestUri!.Query);
    }

    [Fact]
    public async Task Method_ReachesEndpoint()
    {
        var (service, handler) = Build(Endpoint("GET", "POST", "PUT", "PATCH", "DELETE"));

        foreach (var method in new[] { "POST", "put", "PATCH", "DELETE" })
        {
            var result = await Call(service, new { environment = "500", method, key = "1" });
            Assert.False(result.IsError, result.Text);
        }

        Assert.Equal(["POST", "PUT", "PATCH", "DELETE"], handler.Calls.Select(c => c.Request.Method.Method));
    }

    [Fact]
    public async Task UnlistedMethod_ReturnsError()
    {
        var (service, handler) = Build(Endpoint("GET"));

        var result = await Call(service, new { environment = "500", method = "DELETE" });

        Assert.True(result.IsError);
        Assert.Contains("GET", result.Text);
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task BodyOnGet_ReturnsError()
    {
        var (service, handler) = Build(Endpoint("GET", "POST"));

        var result = await Call(service, new { environment = "500", body = "{\"A\":1}" });

        Assert.True(result.IsError);
        Assert.Empty(handler.Calls);
    }

    [Theory]
    [InlineData("42", "/api/500/Items(42)")]
    [InlineData("ABC", "/api/500/Items('ABC')")]
    [InlineData("a b", "/api/500/Items('a%20b')")]
    [InlineData("744276de-4918-4b56-af75-16901371983b", "/api/500/Items(744276de-4918-4b56-af75-16901371983b)")]
    public async Task Key_AddressesRecord(string key, string expected)
    {
        var (service, handler) = Build(Endpoint("GET"));

        var result = await Call(service, new { environment = "500", key });

        Assert.False(result.IsError, result.Text);
        Assert.Equal(expected, handler.Calls.Single().Request.RequestUri!.AbsolutePath);
    }

    [Theory]
    [InlineData("../Other")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("1')/../('2")]
    [InlineData("a\nb")]
    public async Task UnsafeKey_ReturnsError(string key)
    {
        var (service, handler) = Build(Endpoint("GET"));

        var result = await Call(service, new { environment = "500", key });

        Assert.True(result.IsError);
        Assert.Empty(handler.Calls);
    }

    [Theory]
    [InlineData("../Other")]
    [InlineData("a/../../Other")]
    [InlineData("a\\..\\b")]
    [InlineData("a//b")]
    public async Task UnsafePath_ReturnsError(string path)
    {
        var (service, handler) = Build(Endpoint("GET"));

        var result = await Call(service, new { environment = "500", path });

        Assert.True(result.IsError);
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task KeyOnFileTool_ReturnsError()
    {
        var (service, handler) = Build(Endpoint("GET") with { EndpointKind = "file" });

        var result = await Call(service, new { environment = "500", key = "1" });

        Assert.True(result.IsError);
        Assert.Empty(handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task UpstreamFailure_SetsError(HttpStatusCode status)
    {
        var (service, _) = Build(Endpoint("GET"), status: status, response: "{\"error\":\"no\"}");

        var result = await Call(service, new { environment = "500" });

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task UnknownTool_SetsError()
    {
        var (service, _) = Build(Endpoint("GET"));

        var result = await service.ExecuteToolAsync("missing", "{}", "500", BaseUrl, Token, CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task LargeResult_IsValidJson_AndStatesTruncation()
    {
        var rows = string.Join(',', Enumerable.Range(0, 500).Select(i => $"{{\"Id\":{i},\"Name\":\"é€\"}}"));
        var (service, _) = Build(Endpoint("GET"), response: $"{{\"Value\":[{rows}]}}", maxToolResultChars: 200);

        var result = await Call(service, new { environment = "500" });

        Assert.False(result.IsError);
        using var document = JsonDocument.Parse(result.Text);
        Assert.True(document.RootElement.GetProperty("truncated").GetBoolean());
        Assert.Equal(200, document.RootElement.GetProperty("returnedChars").GetInt32());
    }

    [Fact]
    public async Task ResultWithinLimit_IsReturnedUnchanged()
    {
        var (service, _) = Build(Endpoint("GET"), response: "{\"Value\":[1]}");

        var result = await Call(service, new { environment = "500" });

        Assert.Equal("{\"Value\":[1]}", result.Text);
    }

    [Fact]
    public async Task FileTool_CallsListRoute()
    {
        var (service, handler) = Build(Endpoint("GET") with { EndpointKind = "file" });

        var result = await Call(service, new { environment = "500" });

        Assert.False(result.IsError, result.Text);
        Assert.Equal("/api/500/files/Items/list", handler.Calls.Single().Request.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task QueryTool_WithoutBody_SendsJsonWithinPageLimit()
    {
        var (service, handler) = Build(Endpoint("QUERY"));

        var result = await Call(service, new { environment = "500" });

        Assert.False(result.IsError, result.Text);
        var (request, body) = handler.Calls.Single();
        Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
        Assert.Equal(50, JsonDocument.Parse(body!).RootElement.GetProperty("$top").GetInt32());
    }

    [Theory]
    [InlineData("{\"$top\":900}", 200)]
    [InlineData("{\"top\":900}", 200)]
    [InlineData("{\"$top\":5}", 5)]
    [InlineData("{\"$filter\":\"A eq 1\"}", 50)]
    [InlineData("{\"$top\":-1}", 50)]
    public void QueryBody_ClampsTop(string body, int expected)
    {
        using var document = JsonDocument.Parse(McpChatService.EnforceQueryBodyLimit(body, 50, 200));

        Assert.Equal(expected, document.RootElement.GetProperty("$top").GetInt32());
        Assert.False(document.RootElement.TryGetProperty("top", out _));
    }

    [Theory]
    [InlineData("[1]")]
    [InlineData("not json")]
    public void QueryBody_NotAnObject_PassesThrough(string body)
    {
        Assert.Equal(body, McpChatService.EnforceQueryBodyLimit(body, 50, 200));
    }

    [Theory]
    [InlineData("http://0.0.0.0:8080", "", "http://localhost:8080")]
    [InlineData("http://[::]:8080", "", "http://localhost:8080")]
    [InlineData("http://+:8080", "", "http://localhost:8080")]
    [InlineData("http://*:8080", "/gw", "http://localhost:8080/gw")]
    [InlineData("https://api.internal:8443", "", "https://api.internal:8443")]
    public void Loopback_UsesBoundAddress(string address, string pathBase, string expected)
    {
        Assert.Equal(expected, McpLoopback.Resolve([address], new PathString(pathBase), 1234));
    }

    [Fact]
    public void Loopback_PrefersHttp()
    {
        Assert.Equal("http://localhost:80", McpLoopback.Resolve(["https://*:443", "http://*:80"], PathString.Empty, 1234));
    }

    [Fact]
    public void Loopback_IgnoresHostHeader_WhenNoAddressIsKnown()
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("gateway.example", 9999);
        context.Connection.LocalPort = 5123;
        context.RequestServices = new ServiceCollection().BuildServiceProvider();

        Assert.Equal("http://localhost:5123", McpLoopback.BaseUrl(context));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("https://console.example", true)]
    [InlineData("HTTPS://CONSOLE.EXAMPLE", true)]
    [InlineData("https://evil.example", false)]
    [InlineData("null", false)]
    public void Origin_RequiresAllowlistEntry(string origin, bool allowed)
    {
        Assert.Equal(allowed, McpServiceExtensions.IsOriginAllowed(origin, ["https://console.example/"]));
    }

    [Fact]
    public void ServerVersion_IsNotPlaceholder()
    {
        Assert.NotEqual("0.0.0.0", McpServiceExtensions.ServerVersion());
    }

    [Fact]
    public void EndpointInfoResult_HasNoUrl()
    {
        Assert.Null(typeof(EndpointInfoResult).GetProperty("Url"));
    }

    [Fact]
    public void Registry_KeepsAllMethodsOnLookup()
    {
        var registry = new McpEndpointRegistry();
        registry.RegisterEndpoints([Endpoint("GET", "POST")]);

        Assert.Equal(["GET", "POST"], registry.MethodsOf(registry.FindByName("items")!));
    }
}
