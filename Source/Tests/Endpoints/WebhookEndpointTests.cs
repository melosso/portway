using System.Net;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using PortwayApi.Classes;
using PortwayApi.Tests.Base;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

public sealed class WebhookEndpointTests : ApiTestBase, IDisposable
{
    private const string Env = "500";
    private static readonly string Dir = Path.Combine("endpoints", "Webhooks", "Strict");
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"portway_webhook_{Guid.NewGuid():N}.db");

    public WebhookEndpointTests()
    {
        using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
            connection.Execute("CREATE TABLE WebhookData (Id INTEGER PRIMARY KEY, WebhookId TEXT, Payload TEXT, ReceivedAt TEXT)");

        SetAllowedEnvironments(Env);
        Write("Hooks", new { DatabaseObjectName = "WebhookData", AllowedColumns = new[] { "Payments", "orders" }, AllowedEnvironments = new[] { Env } });
        EndpointHandler.ReloadAllEndpoints();
        AddAuthorizationHeader();
    }

    public new void Dispose()
    {
        if (Directory.Exists(Dir)) Directory.Delete(Dir, recursive: true);
        EndpointHandler.ReloadAllEndpoints();
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
        base.Dispose();
    }

    protected override string ConnectionStringFor(string environment) =>
        environment == Env ? $"Data Source={_dbPath}" : base.ConnectionStringFor(environment);

    private static void Write(string name, object content)
    {
        Directory.CreateDirectory(Path.Combine(Dir, name));
        File.WriteAllText(Path.Combine(Dir, name, "entity.json"), JsonSerializer.Serialize(content));
    }

    private Task<HttpResponseMessage> Post(string path, string body) =>
        _client.PostAsync($"/api/{Env}/Strict/{path}", new StringContent(body, Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);

    private List<(string WebhookId, string Payload)> Rows()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        return connection.Query<(string, string)>("SELECT WebhookId, Payload FROM WebhookData ORDER BY Id").ToList();
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("")]
    public async Task MalformedOrEmptyJson_Returns400(string body)
    {
        var response = await Post("Hooks/Payments", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(Rows());
    }

    [Fact]
    public async Task IdenticalBodyTwice_StoresTwoRows()
    {
        await Post("Hooks/Payments", """{"a":1}""");
        await Post("Hooks/Payments", """{"a":1}""");

        Assert.Equal(2, Rows().Count);
    }

    [Fact]
    public async Task Id_StoredWithConfiguredCasing()
    {
        var response = await Post("Hooks/PAYMENTS", """{"a":1}""");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Payments", Rows().Single().WebhookId);
    }

    [Theory]
    [InlineData("Hooks/Payments/extra")]
    [InlineData("Hooks('Payments')")]
    [InlineData("Hooks/'Payments'")]
    [InlineData("Hooks/(Payments)")]
    [InlineData("Hooks(1)/Payments")]
    [InlineData("Hooks/unknown")]
    public async Task NonCanonicalRoute_Returns404(string path)
    {
        var response = await Post(path, """{"a":1}""");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(Rows());
    }

    [Fact]
    public async Task WebhookRoute_ParenthesisedId_Returns404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Post("Hooks(orders)/Payments", """{"a":1}""")).StatusCode);
        Assert.Empty(Rows());
    }

    [Theory]
    [InlineData("\"abc\"")]
    [InlineData("{ \"a\" :  1 ,\n  \"b\": [ 1, 2 ] }")]
    [InlineData("[1,2]")]
    [InlineData("42")]
    public async Task Payload_StoredAsRawBody(string body)
    {
        await Post("Hooks/orders", body);

        Assert.Equal(body, Rows().Single().Payload);
    }

    [Fact]
    public async Task Created_HasNoLocationAndStoredMessage()
    {
        var response = await Post("Hooks/orders", """{"a":1}""");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null(response.Headers.Location);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Webhook stored.", body.RootElement.GetProperty("message").GetString());
        Assert.True(body.RootElement.GetProperty("id").GetInt64() > 0);
    }

    [Fact]
    public async Task EmptyAllowedColumns_DeniesEveryId()
    {
        Write("Hooks", new { DatabaseObjectName = "WebhookData", AllowedColumns = Array.Empty<string>(), AllowedEnvironments = new[] { Env } });
        EndpointHandler.ReloadAllEndpoints();

        Assert.Equal(HttpStatusCode.NotFound, (await Post("Hooks/anything", """{"a":1}""")).StatusCode);
        Assert.Empty(Rows());
    }

    [Fact]
    public async Task AbsentAllowedColumns_DeniesEveryId()
    {
        Write("Hooks", new { DatabaseObjectName = "WebhookData", AllowedEnvironments = new[] { Env } });
        EndpointHandler.ReloadAllEndpoints();

        Assert.Equal(HttpStatusCode.NotFound, (await Post("Hooks/anything", """{"a":1}""")).StatusCode);
        Assert.Empty(Rows());
    }

    [Theory]
    [InlineData("""{"DatabaseObjectName":"WebhookData","AllowedColumns":[]}""")]
    [InlineData("""{"DatabaseObjectName":"WebhookData"}""")]
    public void ConsoleSave_EmptyAllowedColumns_Refused(string json) =>
        Assert.Contains(EndpointHandler.ValidateConfig(EndpointType.Webhook, json), e => e.Contains("AllowedColumns"));
}
