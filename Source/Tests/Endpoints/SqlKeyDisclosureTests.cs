using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Moq;
using PortwayApi.Classes;
using PortwayApi.Helpers;
using PortwayApi.Services.Providers;
using PortwayApi.Tests.Base;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

/// <summary>
/// Key lookups against a real database: responses name public aliases, a malformed key never returns the collection
/// </summary>
public sealed class SqlKeyDisclosureTests : ApiTestBase, IDisposable
{
    private const string Env = "KEY";
    private const string Api = $"/api/{Env}/KeyTest/Catalog";
    private static readonly string EndpointDir = Path.Combine("endpoints", "SQL", "KeyTest");

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"portway_keys_{Guid.NewGuid():N}.db");

    public SqlKeyDisclosureTests()
    {
        using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
        {
            connection.Execute("""
                CREATE TABLE Items (ItemCode TEXT PRIMARY KEY, Description TEXT NOT NULL, Legacy TEXT);
                INSERT INTO Items VALUES ('A1', 'First', 'x'), ('B2', 'Second', 'y');
                """);
        }

        var target = Path.Combine(EndpointDir, "Catalog");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "entity.json"), JsonSerializer.Serialize(new
        {
            DatabaseObjectName = "Items",
            DatabaseObjectType = "Table",
            AllowedEnvironments = new[] { Env },
            AllowedMethods = new[] { "GET", "PUT", "PATCH" },
            WriteMode = "Table",
            PrimaryKey = "ItemCode",
            AllowedColumns = new[] { "ItemCode;ProductNumber", "Description", "Legacy;Id" },
            Documentation = new { MethodDocumentation = new { GET = "You can retrieve all items." } }
        }));
        EndpointHandler.ReloadAllEndpoints();

        SetAllowedEnvironments(Env);
        _mockEnvironmentSettingsProvider
            .Setup(p => p.LoadEnvironmentOrThrowAsync(It.IsAny<string>()))
            .ReturnsAsync(($"Data Source={_dbPath}", "localhost", new Dictionary<string, string>()));

        var converter = new ODataToSqlConverter([new SqliteProvider()]);
        _mockODataToSqlConverter
            .Setup(c => c.ConvertToSQL(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<SqlProviderType>(), It.IsAny<IReadOnlyList<EndpointRelationship>?>()))
            .Returns((string e, Dictionary<string, string> o, SqlProviderType p, IReadOnlyList<EndpointRelationship>? r) => converter.ConvertToSQL(e, o, p, r));
        _mockODataToSqlConverter
            .Setup(c => c.ConvertToSQL(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<SqlProviderType>(), It.IsAny<IReadOnlyList<EndpointRelationship>?>(), It.IsAny<IReadOnlyList<TenantPredicate>>()))
            .Returns((string e, Dictionary<string, string> o, SqlProviderType p, IReadOnlyList<EndpointRelationship>? r, IReadOnlyList<TenantPredicate> t) => converter.ConvertToSQL(e, o, p, r, t));
        _mockODataToSqlConverter
            .Setup(c => c.ConvertToCountSQL(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<SqlProviderType>(), It.IsAny<IReadOnlyList<TenantPredicate>>()))
            .Returns((string e, Dictionary<string, string> o, SqlProviderType p, IReadOnlyList<TenantPredicate> t) => converter.ConvertToCountSQL(e, o, p, t));
    }

    public new void Dispose()
    {
        Directory.Delete(EndpointDir, recursive: true);
        EndpointHandler.ReloadAllEndpoints();
        base.Dispose();
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    private async Task<(HttpStatusCode Status, string Body)> Send(HttpMethod method, string url, string? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        if (body is not null)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task KnownKey_ReturnsTheRecordUnderItsAlias()
    {
        var (status, body) = await Send(HttpMethod.Get, $"{Api}('A1')");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("\"ProductNumber\":\"A1\"", body.Replace(" ", ""));
        Assert.DoesNotContain("ItemCode", body);
        Assert.DoesNotContain("B2", body);
    }

    [Fact]
    public async Task UnknownKey_NamesNeitherColumnNorValue()
    {
        var (status, body) = await Send(HttpMethod.Get, $"{Api}('secret-probe')");

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.DoesNotContain("ItemCode", body);
        Assert.DoesNotContain("secret-probe", body);
    }

    [Fact]
    public async Task MalformedKey_NeverReturnsTheCollection()
    {
        var (status, body) = await Send(HttpMethod.Get, $"{Api}(A1)");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.DoesNotContain("Second", body);
    }

    [Theory]
    [InlineData("A1")]
    [InlineData("(A1)")]
    [InlineData("A1/extra")]
    public async Task UnquotedPathKey_NeverReturnsTheCollection(string suffix)
    {
        var (status, body) = await Send(HttpMethod.Get, $"{Api}/{suffix}");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.DoesNotContain("Second", body);
    }

    [Fact]
    public async Task QuotedPathKey_ReturnsTheRecord()
    {
        var (status, body) = await Send(HttpMethod.Get, $"{Api}/'A1'");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("First", body);
        Assert.DoesNotContain("Second", body);
    }

    [Fact]
    public async Task TableUpdateWithoutKey_NamesTheAlias()
    {
        var (status, body) = await Send(HttpMethod.Patch, Api, """{ "Id": "x", "Description": "Changed" }""");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("ProductNumber", body);
        Assert.DoesNotContain("ItemCode", body);
    }

    [Fact]
    public async Task ByIdOperation_DoesNotReuseTheListDescription()
    {
        var (_, body) = await Send(HttpMethod.Get, "/docs/openapi.json");
        using var document = JsonDocument.Parse(body);
        var paths = document.RootElement.GetProperty("paths");

        Assert.Contains("all items", paths.GetProperty("/api/{env}/KeyTest/Catalog").GetProperty("get").GetProperty("description").GetString());
        Assert.Equal("Returns the record with the given ID.", paths.GetProperty("/api/{env}/KeyTest/Catalog({id})").GetProperty("get").GetProperty("description").GetString());
    }

    private async Task<string?> ByIdTitle()
    {
        var (_, body) = await Send(HttpMethod.Get, "/docs/openapi.json");
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("paths").GetProperty("/api/{env}/KeyTest/Catalog({id})").GetProperty("get").GetProperty("summary").GetString();
    }

    private static void Configure(Action<System.Text.Json.Nodes.JsonObject> change)
    {
        var file = Path.Combine(EndpointDir, "Catalog", "entity.json");
        var entity = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        change(entity);
        File.WriteAllText(file, entity.ToJsonString());
        EndpointHandler.ReloadAllEndpoints();
    }

    [Theory]
    [InlineData("GET:id")]
    [InlineData("GET:ID")]
    [InlineData("get:Id")]
    public async Task ByIdTitle_ComesFromMethodDescriptions(string key)
    {
        Configure(entity => entity["Documentation"]!["MethodDescriptions"] = new System.Text.Json.Nodes.JsonObject
        {
            ["GET"] = "List entries",
            [key] = "Look up one entry"
        });

        Assert.Equal("Look up one entry", await ByIdTitle());
    }

    [Fact]
    public async Task ByIdTitle_WithoutConfiguration_OmitsTheEndpointName()
    {
        Assert.DoesNotContain("Catalog", await ByIdTitle());
    }

    [Fact]
    public async Task ByIdTitle_WithoutMethodDescription_UsesDisplayName()
    {
        Configure(entity => entity["DisplayName"] = "Shelf entry");

        Assert.Contains("Shelf entry", await ByIdTitle());
    }
}
