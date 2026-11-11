using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PortwayApi.Classes;
using PortwayApi.Services;
using PortwayApi.Tests.Base;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

/// <summary>
/// A verified webhook id accepts its own proof of identity only; bearer tokens and environment authentication do not apply
/// </summary>
public sealed class WebhookVerificationTests : ApiTestBase, IDisposable
{
    private const string Env = "500";
    private const string GitHubSecret = "github-hook-secret-000";
    private const string ErpSecret = "erp-shared-secret-0000";
    private const string Body = """{"action":"opened"}""";
    private static readonly string Dir = Path.Combine("endpoints", "Webhooks", "Signed");
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"portway_signed_{Guid.NewGuid():N}.db");

    public WebhookVerificationTests()
    {
        using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
            connection.Execute("CREATE TABLE WebhookData (Id INTEGER PRIMARY KEY, WebhookId TEXT, Payload TEXT, ReceivedAt TEXT)");

        SetAllowedEnvironments(Env, "700");
        Write(Path.Combine(Dir, "Hooks"), new
        {
            DatabaseObjectName = "WebhookData",
            AllowedColumns = new[] { "Payments", "erp", "plain" },
            AllowedEnvironments = new[] { Env },
            Verification = new Dictionary<string, object>
            {
                ["Payments"] = new { Scheme = "GitHub", Secret = "${secret:GH-HOOK}" },
                ["erp"] = new { Scheme = "SharedSecret", Header = "X-Hook-Key", Secret = "${secret:ERP-HOOK}" }
            }
        });
        EndpointHandler.ReloadAllEndpoints();
        _mockEnvironmentSettingsProvider.Setup(p => p.GetSecretAsync(It.IsAny<string>(), "GH-HOOK", It.IsAny<CancellationToken>())).ReturnsAsync(GitHubSecret);
        _mockEnvironmentSettingsProvider.Setup(p => p.GetSecretAsync(It.IsAny<string>(), "ERP-HOOK", It.IsAny<CancellationToken>())).ReturnsAsync(ErpSecret);
        _client.DefaultRequestHeaders.Authorization = null;
    }

    public new void Dispose()
    {
        if (Directory.Exists(Dir)) Directory.Delete(Dir, recursive: true);
        var root = Path.Combine("endpoints", "Webhooks", "RootSigned");
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        EndpointHandler.ReloadAllEndpoints();
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
        base.Dispose();
    }

    protected override string ConnectionStringFor(string environment) => $"Data Source={_dbPath}";

    private static void Write(string dir, object content)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "entity.json"), JsonSerializer.Serialize(content));
    }

    private static string GitHubSignature(string body) =>
        "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(GitHubSecret), Encoding.UTF8.GetBytes(body)));

    private Task<HttpResponseMessage> Post(string path, Action<HttpRequestMessage>? configure = null, string env = Env)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/{env}/Signed/Hooks/{path}") { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
        configure?.Invoke(request);
        return _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private int Rows()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        return connection.ExecuteScalar<int>("SELECT COUNT(*) FROM WebhookData");
    }

    private static void Bearer(HttpRequestMessage r) => r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

    [Fact]
    public async Task ValidSignature_StoredWithoutToken()
    {
        var response = await Post("Payments", r => r.Headers.Add("X-Hub-Signature-256", GitHubSignature(Body)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, Rows());
    }

    [Fact]
    public async Task InvalidSignature_Returns401LikeMissingCredentials()
    {
        var response = await Post("Payments", r => r.Headers.Add("X-Hub-Signature-256", GitHubSignature(Body + " ")));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
        Assert.Contains("Authentication required", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, Rows());
    }

    [Fact]
    public async Task VerifiedId_RejectsBearerToken()
    {
        var response = await Post("Payments", Bearer);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, Rows());
    }

    [Fact]
    public async Task VerifiedId_WrongCase_StillRequiresSignature()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post("PAYMENTS", Bearer)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Post("PAYMENTS", r => r.Headers.Add("X-Hub-Signature-256", GitHubSignature(Body)))).StatusCode);
    }

    [Fact]
    public async Task VerifiedId_DisallowedEnvironment_ReadsNoSecret()
    {
        var response = await Post("Payments", r => r.Headers.Add("X-Hub-Signature-256", GitHubSignature(Body)), env: "700");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        _mockEnvironmentSettingsProvider.Verify(p => p.GetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MissingSignatureHeader_ReadsNoSecret()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post("Payments")).StatusCode);
        _mockEnvironmentSettingsProvider.Verify(p => p.GetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnverifiedId_NoToken_Returns401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post("plain")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Post("plain", Bearer)).StatusCode);
    }

    [Fact]
    public async Task SharedSecret_ScopedToOneId()
    {
        Assert.Equal(HttpStatusCode.Created, (await Post("erp", r => r.Headers.Add("X-Hook-Key", ErpSecret))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post("Payments", r => r.Headers.Add("X-Hook-Key", ErpSecret))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post("plain", r => r.Headers.Add("X-Hook-Key", ErpSecret))).StatusCode);
    }

    [Fact]
    public async Task Verification_MissingSecret_Returns401()
    {
        _mockEnvironmentSettingsProvider.Setup(p => p.GetSecretAsync(It.IsAny<string>(), "GH-HOOK", It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Post("Payments", r => r.Headers.Add("X-Hub-Signature-256", GitHubSignature(Body)))).StatusCode);
    }

    [Fact]
    public void Marker_DifferentId_Rejected()
    {
        var endpoint = EndpointHandler.GetSqlWebhookEndpoints()["Signed/Hooks"];
        var payments = new WebhookRoute(Env, "Signed/Hooks", "payments");

        Assert.True(PortwayApi.Api.EndpointController.AcceptsVerification(payments, endpoint, payments with { Id = "Payments" }));
        Assert.False(PortwayApi.Api.EndpointController.AcceptsVerification(payments, endpoint, payments with { Id = "erp" }));
        Assert.False(PortwayApi.Api.EndpointController.AcceptsVerification(payments, endpoint, null));
        Assert.True(PortwayApi.Api.EndpointController.AcceptsVerification(payments with { Id = "plain" }, endpoint, null));
    }

    [Fact]
    public void RootLevelVerification_LoadError()
    {
        Write(Path.Combine("endpoints", "Webhooks", "RootSigned"), new
        {
            DatabaseObjectName = "WebhookData",
            AllowedColumns = new[] { "a" },
            Verification = new Dictionary<string, object> { ["a"] = new { Scheme = "GitHub", Secret = "${secret:GH-HOOK}" } }
        });
        EndpointHandler.ReloadAllEndpoints();

        Assert.False(EndpointHandler.GetSqlWebhookEndpoints().ContainsKey("RootSigned"));
    }

    [Fact]
    public async Task OpenApi_ListsVerifiedIdsWithHeaderNamesOnly()
    {
        var doc = await _client.GetStringAsync("/docs/openapi.json", TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(doc);
        var description = json.RootElement.GetProperty("paths").GetProperty("/api/{env}/Signed/Hooks/{webhookId}").GetProperty("post").GetProperty("description").GetString();

        Assert.Contains("X-Hub-Signature-256", description);
        Assert.Contains("X-Hook-Key", description);
        Assert.DoesNotContain("GH-HOOK", doc);
        Assert.DoesNotContain("ERP-HOOK", doc);
    }

    [Fact]
    public async Task TrafficLog_RedactsVerificationHeaders()
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<PortwayApi.Middleware.ProxyTrafficLogEntry>();
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton(new Mock<PortwayApi.Interfaces.IEnvironmentSettingsProvider>().Object);
        var options = Microsoft.Extensions.Options.Options.Create(new PortwayApi.Middleware.RequestTrafficLoggingOptions { Enabled = true, CaptureHeaders = true });
        var middleware = new PortwayApi.Middleware.ProxyTrafficLoggerMiddleware(_ => Task.CompletedTask, options, channel, services.BuildServiceProvider());
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = $"/api/{Env}/Signed/Hooks/erp";
        context.Request.Headers["X-Hook-Key"] = ErpSecret;
        context.Request.Headers["X-Hub-Signature-256"] = "sha256=abc";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.True(channel.Reader.TryRead(out var entry));
        Assert.Equal("[REDACTED]", entry!.RequestHeaders["X-Hook-Key"]);
        Assert.Equal("[REDACTED]", entry.RequestHeaders["X-Hub-Signature-256"]);
    }
}
