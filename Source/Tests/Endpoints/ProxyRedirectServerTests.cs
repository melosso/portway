using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using PortwayApi.Classes;
using PortwayApi.Tests.Base;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

/// <summary>
/// Redirect handling against real local servers: a request with a secret follows same-host redirects only
/// </summary>
public sealed class ProxyRedirectServerTests : ApiTestBase, IDisposable
{
    private const string Env = "500";
    private const int SamePort = 8043;
    private const int OtherPort = 8044;
    private static readonly string Dir = Path.Combine("endpoints", "Proxy", "RedirectTest");
    private readonly RedirectServer _same = new(SamePort, OtherPort);
    private readonly RedirectServer _other = new(OtherPort, OtherPort);

    public ProxyRedirectServerTests()
    {
        SetAllowedEnvironments(Env);
        Write("Secret", new Dictionary<string, string> { ["X-Api-Key"] = "${secret:ERP-KEY}" });
        Write("Plain", new Dictionary<string, string> { ["X-Trace"] = "plain" });
        EndpointHandler.ReloadAllEndpoints();
        _mockEnvironmentSettingsProvider.Setup(p => p.GetSecretAsync(Env, "ERP-KEY", It.IsAny<CancellationToken>())).ReturnsAsync("erp-secret");
        AddAuthorizationHeader();
    }

    public new void Dispose()
    {
        Directory.Delete(Dir, recursive: true);
        EndpointHandler.ReloadAllEndpoints();
        _same.Dispose();
        _other.Dispose();
        base.Dispose();
    }

    private static void Write(string name, Dictionary<string, string> headers)
    {
        Directory.CreateDirectory(Path.Combine(Dir, name));
        File.WriteAllText(Path.Combine(Dir, name, "entity.json"), JsonSerializer.Serialize(new
        {
            Url = $"http://localhost:{SamePort}",
            Methods = new[] { "GET", "POST" },
            AllowedEnvironments = new[] { Env },
            Headers = headers
        }));
    }

    private Task<HttpResponseMessage> Send(HttpMethod method, string endpoint, string path) =>
        _client.SendAsync(new HttpRequestMessage(method, $"/api/{Env}/RedirectTest/{endpoint}/{path}")
        {
            Content = method == HttpMethod.Post ? new StringContent("{}", System.Text.Encoding.UTF8, "application/json") : null
        }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(303)]
    [InlineData(307)]
    [InlineData(308)]
    public async Task SameHostRedirect_FollowedWithSecret(int status)
    {
        var response = await Send(HttpMethod.Get, "Secret", $"same{status}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("erp-secret", _same.FinalApiKey);
    }

    [Theory]
    [InlineData(302)]
    [InlineData(307)]
    public async Task CrossHostRedirect_ReturnedToClientWithSecret(int status)
    {
        using var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-token");

        var response = await client.GetAsync($"/api/{Env}/RedirectTest/Secret/cross{status}", TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        Assert.Equal(0, _other.Hits);
    }

    [Fact]
    public async Task CrossHostRedirect_FollowedWithoutSecret()
    {
        var response = await Send(HttpMethod.Get, "Plain", "cross302");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, _other.Hits);
    }

    [Theory]
    [InlineData(303, "GET")]
    [InlineData(307, "POST")]
    [InlineData(308, "POST")]
    public async Task PostRedirect_MethodFollowsStatus(int status, string expected)
    {
        await Send(HttpMethod.Post, "Secret", $"same{status}");

        Assert.Equal(expected, _same.FinalMethod);
    }

    private sealed class RedirectServer : IDisposable
    {
        private readonly WebApplication _app;

        public int Hits;
        public string? FinalApiKey { get; private set; }
        public string? FinalMethod { get; private set; }

        public RedirectServer(int port, int otherPort)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseKestrel(k => k.ListenLocalhost(port));
            _app = builder.Build();
            _app.Run(ctx =>
            {
                Interlocked.Increment(ref Hits);
                var segment = ctx.Request.Path.Value!.Trim('/').Split('/')[^1];
                if (segment.StartsWith("same"))
                {
                    ctx.Response.StatusCode = int.Parse(segment[4..]);
                    ctx.Response.Headers.Location = "/final";
                    return Task.CompletedTask;
                }
                if (segment.StartsWith("cross"))
                {
                    ctx.Response.StatusCode = int.Parse(segment[5..]);
                    ctx.Response.Headers.Location = $"http://localhost:{otherPort}/final";
                    return Task.CompletedTask;
                }

                FinalApiKey = ctx.Request.Headers["X-Api-Key"];
                FinalMethod = ctx.Request.Method;
                ctx.Response.ContentType = "application/json";
                return ctx.Response.WriteAsync("""{"ok":true}""");
            });
            _app.StartAsync().GetAwaiter().GetResult();
        }

        public void Dispose() => _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
