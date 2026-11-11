using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Moq;
using PortwayApi.Classes;
using PortwayApi.Interfaces;
using PortwayApi.Services.Providers;
using PortwayApi.Tests.Base;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

/// <summary>
/// Reads after a write return the written state
/// </summary>
public sealed class CacheConsistencyTests : ApiTestBase, IDisposable
{
    private const string Env = "CC";
    private const int UpstreamPort = 8031;
    private static readonly string SqlDir = Path.Combine("endpoints", "SQL", "CacheCheck");
    private static readonly string ProxyDir = Path.Combine("endpoints", "Proxy", "CacheCheck");
    private static readonly string WebhookDir = Path.Combine("endpoints", "Webhooks", "CacheCheck");
    private static readonly string StaticDir = Path.Combine("endpoints", "Static", "CacheCheck");
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"portway_cache_{Guid.NewGuid():N}.db");
    private readonly UpstreamCapture _upstream = new(UpstreamPort);

    public CacheConsistencyTests()
    {
        using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE Items (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL);
                CREATE VIEW ItemsView AS SELECT Id, Name FROM Items;
                CREATE TABLE WebhookData (Id INTEGER PRIMARY KEY, WebhookId TEXT, Payload TEXT, ReceivedAt TEXT);
                INSERT INTO Items (Name) VALUES ('a');
                """;
            command.ExecuteNonQuery();
        }

        SetAllowedEnvironments(Env);
        UseRealConverter(_mockODataToSqlConverter);
        string[] env = [Env];
        Write(SqlDir, "Items", new { DatabaseObjectName = "Items", AllowedEnvironments = env, AllowedMethods = new[] { "GET", "POST", "PUT", "DELETE" }, WriteMode = "Table", PrimaryKey = "Id", AllowedColumns = new[] { "Id", "Name" } });
        Write(SqlDir, "ItemsView", new { DatabaseObjectName = "ItemsView", DatabaseObjectType = "View", AllowedEnvironments = env, AllowedMethods = new[] { "GET" }, AllowedColumns = new[] { "Id", "Name" }, Properties = new { CacheInvalidatedBy = new[] { "Items" } } });
        Write(SqlDir, "ItemsWriter", new { DatabaseObjectName = "Items", AllowedEnvironments = env, AllowedMethods = new[] { "POST" }, WriteMode = "Table", PrimaryKey = "Id", AllowedColumns = new[] { "Id", "Name" }, Properties = new { CacheEnabled = false } });
        Write(SqlDir, "BadLink", new { DatabaseObjectName = "Items", AllowedEnvironments = env, AllowedMethods = new[] { "GET" }, AllowedColumns = new[] { "Id", "Name" }, Properties = new { CacheInvalidatedBy = new[] { "Items; DROP TABLE Items" } } });
        Write(SqlDir, "Hooks", new { DatabaseObjectName = "WebhookData", AllowedEnvironments = env, AllowedMethods = new[] { "GET" }, AllowedColumns = new[] { "Id", "WebhookId" } });
        Write(WebhookDir, "Hook", new { DatabaseObjectName = "WebhookData", AllowedColumns = new[] { "evt" }, AllowedEnvironments = env });
        Write(ProxyDir, "Accounts", new { Url = $"http://localhost:{UpstreamPort}/accounts", Methods = new[] { "GET", "POST" }, AllowedEnvironments = env });
        Write(ProxyDir, "Flow", new
        {
            Type = "Composite",
            Url = $"http://localhost:{UpstreamPort}/flow",
            Methods = new[] { "POST" },
            AllowedEnvironments = env,
            CompositeConfig = new { Name = "Flow", Steps = new[] { new { Name = "Create", Endpoint = "CacheCheck/Accounts", Method = "POST" } } }
        });
        Write(StaticDir, "Doc", new { ContentType = "application/json", ContentFile = "doc.json", AllowedEnvironments = env });
        File.WriteAllText(Path.Combine(StaticDir, "Doc", "doc.json"), """{"version":1}""");
        EndpointHandler.ReloadAllEndpoints();
    }

    public new void Dispose()
    {
        foreach (var dir in new[] { SqlDir, ProxyDir, WebhookDir, StaticDir })
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        EndpointHandler.ReloadAllEndpoints();
        _upstream.Dispose();
        base.Dispose();
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    protected override string ConnectionStringFor(string environment) =>
        environment == Env ? $"Data Source={_dbPath}" : base.ConnectionStringFor(environment);

    private static void UseRealConverter(Mock<IODataToSqlConverter> mock)
    {
        var converter = new ODataToSqlConverter([new SqliteProvider()]);
        mock.Setup(c => c.ConvertToSQL(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<SqlProviderType>(), It.IsAny<IReadOnlyList<EndpointRelationship>?>()))
            .Returns((string e, Dictionary<string, string> o, SqlProviderType p, IReadOnlyList<EndpointRelationship>? r) => converter.ConvertToSQL(e, o, p, r));
    }

    private static void Write(string root, string name, object entity)
    {
        Directory.CreateDirectory(Path.Combine(root, name));
        File.WriteAllText(Path.Combine(root, name, "entity.json"), JsonSerializer.Serialize(entity));
    }

    private int RowCount(string table)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private async Task<int> Count(string path)
    {
        var response = await _client.GetAsync($"/api/{Env}/CacheCheck/{path}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return doc.RootElement.GetProperty("value").GetArrayLength();
    }

    private async Task Post(string path, string json)
    {
        var response = await _client.PostAsync($"/api/{Env}/CacheCheck/{path}", new StringContent(json, Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode, $"{path}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)}");
    }

    [Theory]
    [InlineData("POST", "{ not json")]
    [InlineData("POST", "")]
    [InlineData("PUT", "{ not json")]
    public async Task SqlWrite_MalformedJson_Returns400(string method, string body)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), $"/api/{Env}/CacheCheck/Items") { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, RowCount("Items"));
    }

    [Fact]
    public async Task TableWrite_RefreshesSameTableReads()
    {
        Assert.Equal(1, await Count("Items"));
        await Post("Items", """{"Name":"b"}""");
        Assert.Equal(2, await Count("Items"));
    }

    [Fact]
    public async Task TableWrite_RefreshesViewReads()
    {
        Assert.Equal(1, await Count("ItemsView"));
        await Post("Items", """{"Name":"b"}""");
        Assert.Equal(2, await Count("ItemsView"));
    }

    [Fact]
    public async Task UncachedWriter_RefreshesCachedReadsOfSameTable()
    {
        Assert.Equal(1, await Count("Items"));
        await Post("ItemsWriter", """{"Name":"b"}""");
        Assert.Equal(2, await Count("Items"));
    }

    [Fact]
    public async Task InvalidCacheInvalidatedByEntry_DoesNotLoadEndpoint()
    {
        var response = await _client.GetAsync($"/api/{Env}/CacheCheck/BadLink", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task WebhookInsert_RefreshesTableReads()
    {
        Assert.Equal(0, await Count("Hooks"));
        await Post("Hook/evt", """{"a":1}""");
        Assert.Equal(1, RowCount("WebhookData"));
        Assert.Equal(1, await Count("Hooks"));
    }

    [Fact]
    public async Task CompositeWriteStep_RefreshesProxyReads()
    {
        int Hits() => _upstream.Requests.Count(r => r.StartsWith("/accounts?"));
        await _client.GetAsync($"/api/{Env}/CacheCheck/Accounts?$top=3", TestContext.Current.CancellationToken);
        await _client.GetAsync($"/api/{Env}/CacheCheck/Accounts?$top=3", TestContext.Current.CancellationToken);
        Assert.Equal(1, Hits());

        await Post("Flow", "{}");

        await _client.GetAsync($"/api/{Env}/CacheCheck/Accounts?$top=3", TestContext.Current.CancellationToken);
        Assert.Equal(2, Hits());
    }

    [Fact]
    public async Task StaticFileEdit_IsServed()
    {
        async Task<string> Read() => await _client.GetStringAsync($"/api/{Env}/CacheCheck/Doc", TestContext.Current.CancellationToken);
        Assert.Contains("\"version\":1", await Read());

        File.WriteAllText(Path.Combine(StaticDir, "Doc", "doc.json"), """{"version":2}""");
        var deadline = DateTime.UtcNow.AddSeconds(5);
        string body;
        do
        {
            body = await Read();
            if (body.Contains("\"version\":2")) break;
            await Task.Delay(250, TestContext.Current.CancellationToken);
        } while (DateTime.UtcNow < deadline);

        Assert.Contains("\"version\":2", body);
    }
}
