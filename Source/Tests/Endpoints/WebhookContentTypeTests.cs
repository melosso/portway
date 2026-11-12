using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Moq;
using PortwayApi.Classes;
using PortwayApi.Tests.Base;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

public sealed class WebhookContentTypeTests : ApiTestBase, IDisposable
{
    private const string Env = "500";
    private const string Secret = "content-type-secret-000";
    private static readonly string Dir = Path.Combine("endpoints", "Webhooks", "Typed");
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"portway_typed_{Guid.NewGuid():N}.db");

    public WebhookContentTypeTests()
    {
        using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
            connection.Execute("CREATE TABLE WebhookData (Id INTEGER PRIMARY KEY, WebhookId TEXT, Payload TEXT, ReceivedAt TEXT)");

        SetAllowedEnvironments(Env);
        Write("Hooks", new
        {
            DatabaseObjectName = "WebhookData",
            AllowedColumns = new[] { "form", "json", "slack", "plain" },
            AllowedEnvironments = new[] { Env },
            Verification = new Dictionary<string, object>
            {
                ["form"] = new { Scheme = "GitHub", Secret = "${secret:HOOK}", AcceptForm = true },
                ["json"] = new { Scheme = "GitHub", Secret = "${secret:HOOK}" },
                ["slack"] = new { Scheme = "Slack", Secret = "${secret:HOOK}" }
            }
        });
        Write("Quiet", new { DatabaseObjectName = "WebhookData", AllowedColumns = new[] { "plain" }, AllowedEnvironments = new[] { Env }, Hidden = true });
        EndpointHandler.ReloadAllEndpoints();
        _mockEnvironmentSettingsProvider.Setup(p => p.GetSecretAsync(It.IsAny<string>(), "HOOK", It.IsAny<CancellationToken>())).ReturnsAsync(Secret);
    }

    public new void Dispose()
    {
        if (Directory.Exists(Dir)) Directory.Delete(Dir, recursive: true);
        EndpointHandler.ReloadAllEndpoints();
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
        base.Dispose();
    }

    protected override string ConnectionStringFor(string environment) => $"Data Source={_dbPath}";

    private static void Write(string name, object content)
    {
        Directory.CreateDirectory(Path.Combine(Dir, name));
        File.WriteAllText(Path.Combine(Dir, name, "entity.json"), JsonSerializer.Serialize(content));
    }

    private static string Hex(string content) => Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes(content)));

    private Task<HttpResponseMessage> Post(string path, string body, string contentType, Action<HttpRequestMessage>? configure = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/{Env}/Typed/{path}") { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body)) };
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        configure?.Invoke(request);
        return _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static Action<HttpRequestMessage> GitHub(string body) => r => r.Headers.Add("X-Hub-Signature-256", "sha256=" + Hex(body));

    private static void Bearer(HttpRequestMessage r) => r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

    private List<string> Payloads()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        return connection.Query<string>("SELECT Payload FROM WebhookData ORDER BY Id").ToList();
    }

    [Fact]
    public async Task Form_OnAcceptingId_StoredRaw()
    {
        const string body = "payload=%7B%22action%22%3A%22opened%22%7D";

        var response = await Post("Hooks/form", body, "application/x-www-form-urlencoded", GitHub(body));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal([body], Payloads());
    }

    [Fact]
    public async Task Form_OnUnverifiedId_Returns415()
    {
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await Post("Hooks/plain", "a=1", "application/x-www-form-urlencoded", Bearer)).StatusCode);
        Assert.Empty(Payloads());
    }

    [Fact]
    public async Task Form_OnVerifiedIdWithoutAcceptForm_Returns415()
    {
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await Post("Hooks/json", "a=1", "application/x-www-form-urlencoded", GitHub("a=1"))).StatusCode);
        Assert.Empty(Payloads());
    }

    [Theory]
    [InlineData("application/vnd.github+json")]
    [InlineData("application/cloudevents+json; charset=utf-8")]
    public async Task PlusJson_Accepted(string contentType)
    {
        const string body = """{"a":1}""";

        Assert.Equal(HttpStatusCode.Created, (await Post("Hooks/json", body, contentType, GitHub(body))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Post("Hooks/plain", body, contentType, Bearer)).StatusCode);
    }

    [Fact]
    public async Task Hidden_Webhook_NoContentTypeRelaxation()
    {
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await Post("Quiet/plain", """{"a":1}""", "application/vnd.github+json", Bearer)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Post("Quiet/plain", """{"a":1}""", "application/json", Bearer)).StatusCode);
    }

    [Fact]
    public void AcceptForm_OnHiddenWebhook_LoadError() =>
        Assert.Contains(EndpointHandler.ValidateConfig(EndpointType.Webhook,
            """{"DatabaseObjectName":"WebhookData","Hidden":true,"AllowedColumns":["a"],"Verification":{"a":{"Scheme":"GitHub","Secret":"${secret:HOOK}","AcceptForm":true}}}"""),
            e => e.Contains("AcceptForm"));

    [Fact]
    public async Task OtherContentTypes_StillRefused() =>
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await Post("Hooks/json", "<a/>", "application/xml", GitHub("<a/>"))).StatusCode);

    private Action<HttpRequestMessage> Slack(string body, long timestamp) => r =>
    {
        r.Headers.Add("X-Slack-Request-Timestamp", timestamp.ToString());
        r.Headers.Add("X-Slack-Signature", "v0=" + Hex($"v0:{timestamp}:{body}"));
    };

    [Fact]
    public async Task SlackUrlVerification_ReturnsChallengeAndStoresNothing()
    {
        const string body = """{"token":"t","challenge":"3eZbrw1aBm2rZgRNFdxV2595E9CY3gmdALWMmHkvFXO7tYXAYM8P","type":"url_verification"}""";

        var response = await Post("Hooks/slack", body, "application/json", Slack(body, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("3eZbrw1aBm2rZgRNFdxV2595E9CY3gmdALWMmHkvFXO7tYXAYM8P", json.RootElement.GetProperty("challenge").GetString());
        Assert.Empty(Payloads());
    }

    [Fact]
    public async Task SlackUrlVerification_Unsigned_Returns401()
    {
        const string body = """{"challenge":"c","type":"url_verification"}""";

        Assert.Equal(HttpStatusCode.Unauthorized, (await Post("Hooks/slack", body, "application/json", Bearer)).StatusCode);
    }

    [Fact]
    public async Task SlackEvent_IsStored()
    {
        const string body = """{"type":"event_callback","event_id":"Ev1"}""";

        Assert.Equal(HttpStatusCode.Created, (await Post("Hooks/slack", body, "application/json", Slack(body, DateTimeOffset.UtcNow.ToUnixTimeSeconds()))).StatusCode);
        Assert.Single(Payloads());
    }
}
