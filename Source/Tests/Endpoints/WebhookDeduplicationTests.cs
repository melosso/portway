using System.Net;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Moq;
using PortwayApi.Classes;
using PortwayApi.Tests.Base;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

public sealed class WebhookDeduplicationTests : ApiTestBase, IDisposable
{
    private const string Env = "500";
    private const string Secret = "dedupe-shared-secret-00";
    private static readonly string Dir = Path.Combine("endpoints", "Webhooks", "Dedupe");
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"portway_dedupe_{Guid.NewGuid():N}.db");

    public WebhookDeduplicationTests()
    {
        using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
            connection.Execute("""
                CREATE TABLE WebhookData (Id INTEGER PRIMARY KEY, WebhookId TEXT, Payload TEXT, ReceivedAt TEXT, DeliveryId TEXT NULL);
                CREATE UNIQUE INDEX UX_WebhookData_Delivery ON WebhookData (WebhookId, DeliveryId);
                """);

        SetAllowedEnvironments(Env);
        Directory.CreateDirectory(Path.Combine(Dir, "Hooks"));
        File.WriteAllText(Path.Combine(Dir, "Hooks", "entity.json"), JsonSerializer.Serialize(new
        {
            DatabaseObjectName = "WebhookData",
            AllowedColumns = new[] { "header", "body", "field" },
            AllowedEnvironments = new[] { Env },
            Verification = new Dictionary<string, object>
            {
                ["header"] = new { Scheme = "SharedSecret", Header = "X-Hook-Key", Secret = "${secret:HOOK}", DeliveryIdHeader = "X-Message-Id", Deduplicate = true },
                ["body"] = new { Scheme = "SharedSecret", Header = "X-Hook-Key", Secret = "${secret:HOOK}", DeliveryIdHeader = "X-Message-Id", DedupeKey = "BodyHash", Deduplicate = true },
                ["field"] = new { Scheme = "SharedSecret", Header = "X-Hook-Key", Secret = "${secret:HOOK}", DeliveryIdField = "id", Deduplicate = true }
            }
        }));
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

    private Task<HttpResponseMessage> Post(string id, string body, string? messageId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/{Env}/Dedupe/Hooks/{id}") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-Hook-Key", Secret);
        if (messageId is not null)
            request.Headers.Add("X-Message-Id", messageId);
        return _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private List<string?> DeliveryIds()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        return connection.Query<string?>("SELECT DeliveryId FROM WebhookData ORDER BY Id").ToList();
    }

    [Fact]
    public async Task HeaderKey_DuplicateDelivery_Returns200AndStoresOnce()
    {
        Assert.Equal(HttpStatusCode.Created, (await Post("header", """{"a":1}""", "msg-1")).StatusCode);

        var duplicate = await Post("header", """{"a":1}""", "msg-1");

        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        using var body = JsonDocument.Parse(await duplicate.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Duplicate delivery ignored.", body.RootElement.GetProperty("message").GetString());
        Assert.False(body.RootElement.TryGetProperty("id", out _));
        Assert.Equal(["msg-1"], DeliveryIds());
    }

    [Fact]
    public async Task HeaderKey_NewDeliveryId_StoresNewRow()
    {
        await Post("header", """{"a":1}""", "msg-1");
        await Post("header", """{"a":1}""", "msg-2");

        Assert.Equal(["msg-1", "msg-2"], DeliveryIds());
    }

    [Fact]
    public async Task BodyHashKey_BlocksReplayWithNewDeliveryId()
    {
        Assert.Equal(HttpStatusCode.Created, (await Post("body", """{"a":1}""", "msg-1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post("body", """{"a":1}""", "msg-2")).StatusCode);

        var stored = Assert.Single(DeliveryIds());
        Assert.Equal(64, stored!.Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task MissingDeliveryId_Returns400(string? messageId)
    {
        var response = await Post("header", """{"a":1}""", messageId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(DeliveryIds());
    }

    [Fact]
    public async Task DeliveryIdOver255Characters_Returns400()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await Post("header", """{"a":1}""", new string('x', 256))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Post("header", """{"a":1}""", new string('x', 255))).StatusCode);
    }

    [Theory]
    [InlineData("""{"id":"evt_1"}""", "evt_1")]
    [InlineData("""{"id":42}""", "42")]
    public async Task FieldKey_ReadsTopLevelField(string body, string expected)
    {
        Assert.Equal(HttpStatusCode.Created, (await Post("field", body)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post("field", body)).StatusCode);
        Assert.Equal([expected], DeliveryIds());
    }

    [Theory]
    [InlineData("""{"nested":{"id":"x"}}""")]
    [InlineData("""{"id":{"x":1}}""")]
    [InlineData("""[1]""")]
    public async Task FieldKey_MissingOrNotScalar_Returns400(string body) =>
        Assert.Equal(HttpStatusCode.BadRequest, (await Post("field", body)).StatusCode);

    [Fact]
    public async Task ConcurrentIdenticalDeliveries_StoreOneRow()
    {
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Post("header", """{"a":1}""", "msg-concurrent")));

        Assert.Single(DeliveryIds());
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(responses, r => Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK));
    }
}
