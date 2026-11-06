using System.Net;
using System.Net.Http.Headers;
using Moq;
using PortwayApi.Classes;
using PortwayApi.Helpers;
using PortwayApi.Tests.Base;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

/// <summary>
/// Credentials that authenticated a request to Portway do not reach the upstream
/// </summary>
public sealed class ProxyCredentialForwardingTests : ApiTestBase, IDisposable
{
    private const string Env = "500";
    private const int UpstreamPort = 8023;
    private static readonly string Dir = Path.Combine("endpoints", "Proxy", "ForwardTest");
    private readonly UpstreamCapture _upstream = new(UpstreamPort);

    public ProxyCredentialForwardingTests()
    {
        SetAllowedEnvironments(Env);
        Write("Plain", false, "");
        Write("Keep", true, "");
        Write("Baked", false, "?api_key=baked-secret");
        EndpointHandler.ReloadAllEndpoints();
    }

    public new void Dispose()
    {
        Directory.Delete(Dir, recursive: true);
        EndpointHandler.ReloadAllEndpoints();
        _upstream.Dispose();
        base.Dispose();
    }

    private static void Write(string name, bool forward, string query)
    {
        Directory.CreateDirectory(Path.Combine(Dir, name));
        File.WriteAllText(Path.Combine(Dir, name, "entity.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            Url = $"http://localhost:{UpstreamPort}/{name}{query}",
            Methods = new[] { "GET" },
            AllowedEnvironments = new[] { Env },
            ForwardAuthorization = forward
        }));
    }

    private void UseEnvironmentAuth(params AuthenticationMethod[] methods) =>
        _mockEnvironmentSettingsProvider.Setup(p => p.GetEnvironmentConfigAsync(It.IsAny<string>()))
            .ReturnsAsync(new EnvironmentConfig { Authentication = new AuthenticationSettings { Enabled = true, Methods = [.. methods] } });

    private Task<HttpResponseMessage> Get(string endpoint, string query = "", Action<HttpRequestMessage>? configure = null, bool token = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/{Env}/ForwardTest/{endpoint}{query}");
        if (token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        configure?.Invoke(request);
        return _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task PortwayToken_DoesNotReachUpstream()
    {
        var response = await Get("Plain");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(_upstream.Headers.ContainsKey("Authorization"));
    }

    [Fact]
    public async Task ForwardAuthorization_RestoresToken()
    {
        var response = await Get("Keep");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Bearer test-token", _upstream.Headers["Authorization"]);
    }

    [Fact]
    public async Task EnvironmentBearer_DoesNotReachUpstream()
    {
        UseEnvironmentAuth(new AuthenticationMethod { Type = "Bearer", Value = "env-bearer" });

        var response = await Get("Plain", configure: r => r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "env-bearer"), token: false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(_upstream.Headers.ContainsKey("Authorization"));
    }

    [Fact]
    public async Task AuthorizationOfAnotherScheme_IsForwarded_AfterApiKeyAuthentication()
    {
        UseEnvironmentAuth(new AuthenticationMethod { Type = "ApiKey", Name = "X-Env-Key", Value = "env-key" });

        var response = await Get("Plain", configure: r =>
        {
            r.Headers.Add("X-Env-Key", "env-key");
            r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "upstream-user");
        }, token: false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Bearer upstream-user", _upstream.Headers["Authorization"]);
        Assert.False(_upstream.Headers.ContainsKey("X-Env-Key"));
    }

    [Theory]
    [InlineData("?env_key=env-key&keep=1")]
    [InlineData("?env%5Fkey=env-key&keep=1")]
    public async Task EnvironmentApiKeyInQuery_DoesNotReachUpstream(string query)
    {
        UseEnvironmentAuth(new AuthenticationMethod { Type = "ApiKey", Name = "env_key", In = "Query", Value = "env-key" });

        var response = await Get("Plain", query, token: false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("env-key", _upstream.PathAndQuery);
        Assert.Contains("keep=1", _upstream.PathAndQuery);
    }

    [Fact]
    public async Task EnvironmentCookie_IsRemoved_OtherCookiesStay()
    {
        UseEnvironmentAuth(new AuthenticationMethod { Type = "ApiKey", Name = "env_key", In = "Cookie", Value = "env-key" });

        var response = await Get("Plain", configure: r => r.Headers.Add("Cookie", "env_key=env-key; theme=dark"), token: false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("theme=dark", _upstream.Headers["Cookie"]);
    }

    [Fact]
    public void Hmac_NamesSignatureAndTimestamp_BasicUsernameIsNoHeader()
    {
        var names = new AuthenticationSettings
        {
            Enabled = true,
            Methods =
            [
                new AuthenticationMethod { Type = "HMAC", Name = "X-Signature|sha256" },
                new AuthenticationMethod { Type = "Basic", Name = "Accept" }
            ]
        }.CredentialNames();

        Assert.Equal(["X-Signature", "X-Timestamp"], names.Headers.Order());
    }

    [Fact]
    public async Task DebugLog_HoldsNoQueryValue()
    {
        var sink = new CollectingSink();
        var previous = Log.Logger;
        Log.Logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(sink).CreateLogger();
        try
        {
            var response = await Get("Baked", "?client=client-secret");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("baked-secret", _upstream.PathAndQuery);
        }
        finally
        {
            Log.Logger = previous;
        }

        Assert.DoesNotContain(sink.Messages, m => m.Contains("baked-secret") || m.Contains("client-secret"));
        Assert.Contains(sink.Messages, m => m.Contains("api_key=***"));
    }

    [Theory]
    [InlineData("http://x/a", "http://x/a")]
    [InlineData("http://x/a?k=v&b=c", "http://x/a?k=***&b=***")]
    [InlineData("http://x/a?flag", "http://x/a?flag")]
    [InlineData(null, "")]
    [InlineData("http://svc:secret@x/a", "http://***@x/a")]
    [InlineData("https://svc:secret@x:8443/a?k=v", "https://***@x:8443/a?k=***")]
    [InlineData("http://x/a@b", "http://x/a@b")]
    public void Redact_RemovesEveryQueryValue(string? url, string expected)
    {
        Assert.Equal(expected, UrlRedaction.Redact(url));
    }

    private sealed class CollectingSink : ILogEventSink
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _messages = new();
        public IEnumerable<string> Messages => _messages;
        public void Emit(LogEvent logEvent) => _messages.Enqueue(logEvent.RenderMessage());
    }
}
