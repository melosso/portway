using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Moq;
using PortwayApi.Auth;
using PortwayApi.Classes;
using PortwayApi.Helpers;
using PortwayApi.Services.Providers;
using PortwayApi.Tests.Base;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

/// <summary>
/// OData read options against SQLite
/// </summary>
public sealed class SqlODataReadTests : ApiTestBase, IDisposable
{
    private const string Env = "ODR";
    private const string Warehouses = $"/api/{Env}/OdataRead/Warehouses";
    private const string ScopedToken = "scoped-token";
    private static readonly string EndpointDir = Path.Combine("endpoints", "SQL", "OdataRead");

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"portway_odr_{Guid.NewGuid():N}.db");

    public SqlODataReadTests()
    {
        using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
        {
            connection.Execute("""
                CREATE TABLE Warehouses (Id INTEGER PRIMARY KEY, Code TEXT NOT NULL, CountryCode TEXT NOT NULL);
                INSERT INTO Warehouses VALUES (1, 'A', 'NL'), (2, 'B', 'DE'), (3, 'C', 'SG');
                CREATE TABLE Countries (Code TEXT PRIMARY KEY, Name TEXT NOT NULL, Secret TEXT);
                INSERT INTO Countries VALUES ('NL', 'Netherlands', 's1'), ('DE', 'Germany', 's2');
                """);
        }

        object Relation(string name, string target) => new { Name = name, Target = $"OdataRead/{target}", LocalColumn = "CountryCode", TargetColumn = "Code" };
        WriteEndpoint("Warehouses", new
        {
            DatabaseObjectName = "Warehouses",
            DatabaseObjectType = "Table",
            AllowedEnvironments = new[] { Env },
            AllowedMethods = new[] { "GET", "QUERY" },
            PrimaryKey = "Id",
            AllowedColumns = new[] { "Id", "Code", "CountryCode" },
            Relationships = new[] { Relation("CountryInfo", "Countries"), Relation("Hidden", "HiddenCountries"), Relation("OtherEnv", "OtherEnvCountries") }
        });
        WriteEndpoint("Countries", Countries());
        WriteEndpoint("HiddenCountries", Countries(enabled: false));
        WriteEndpoint("OtherEnvCountries", Countries(environment: "OTHER"));
        EndpointHandler.ReloadAllEndpoints();

        SetAllowedEnvironments(Env, "OTHER");
        _mockEnvironmentSettingsProvider
            .Setup(p => p.LoadEnvironmentOrThrowAsync(It.IsAny<string>()))
            .ReturnsAsync(($"Data Source={_dbPath}", "localhost", new Dictionary<string, string>()));

        var converter = new ODataToSqlConverter([new SqliteProvider()]);
        _mockODataToSqlConverter
            .Setup(c => c.ConvertToSQL(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<SqlProviderType>(), It.IsAny<IReadOnlyList<EndpointRelationship>?>()))
            .Returns((string e, Dictionary<string, string> o, SqlProviderType p, IReadOnlyList<EndpointRelationship>? r) => converter.ConvertToSQL(e, o, p, r));
        _mockODataToSqlConverter
            .Setup(c => c.ConvertToCountSQL(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<SqlProviderType>()))
            .Returns((string e, Dictionary<string, string> o, SqlProviderType p) => converter.ConvertToCountSQL(e, o, p));

        _mockTokenService.Setup(s => s.VerifyTokenAsync(ScopedToken)).ReturnsAsync(true);
        _mockTokenService.Setup(s => s.GetTokenDetailsByTokenAsync(ScopedToken)).ReturnsAsync(new AuthToken
        {
            Username = "scoped-user",
            TokenHash = "hash",
            TokenSalt = "salt",
            AllowedEnvironments = "*",
            AllowedScopes = "OdataRead/Warehouses"
        });
    }

    private static object Countries(bool enabled = true, string environment = Env) => new
    {
        Enabled = enabled,
        DatabaseObjectName = "Countries",
        DatabaseObjectType = "Table",
        AllowedEnvironments = new[] { environment },
        AllowedMethods = new[] { "GET" },
        PrimaryKey = "Code",
        AllowedColumns = new[] { "Code", "Name;CountryName" }
    };

    private static void WriteEndpoint(string name, object definition)
    {
        var target = Path.Combine(EndpointDir, name);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "entity.json"), JsonSerializer.Serialize(definition));
    }

    public new void Dispose()
    {
        Directory.Delete(EndpointDir, recursive: true);
        EndpointHandler.ReloadAllEndpoints();
        base.Dispose();
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    private async Task<(HttpStatusCode Status, JsonElement Body, string Raw)> Send(string url, string? queryBody = null, string token = "test-token")
    {
        using var request = new HttpRequestMessage(queryBody is null ? HttpMethod.Get : new HttpMethod("QUERY"), url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (queryBody is not null)
            request.Content = new StringContent(queryBody, Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return (response.StatusCode, raw.Length > 0 && raw[0] == '{' ? JsonDocument.Parse(raw).RootElement.Clone() : default, raw);
    }

    private static JsonElement Row(JsonElement body, string code) =>
        body.GetProperty("value").EnumerateArray().Single(r => r.GetProperty("Code").GetString() == code);

    [Fact]
    public async Task ExpandKeepsRootRowsWithoutMatch()
    {
        var (status, body, raw) = await Send($"{Warehouses}?$expand=CountryInfo&$count=true");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(3, body.GetProperty("count").GetInt32());
        Assert.Equal(3, body.GetProperty("totalCount").GetInt64());
        Assert.Equal(JsonValueKind.Null, Row(body, "C").GetProperty("CountryInfo").ValueKind);
        Assert.Equal("Netherlands", Row(body, "A").GetProperty("CountryInfo").GetProperty("CountryName").GetString());
        Assert.DoesNotContain("s1", raw);
    }

    [Fact]
    public async Task DuplicateNavigationIsExpandedOnce()
    {
        var (status, body, _) = await Send($"{Warehouses}?$expand=CountryInfo,countryinfo");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(3, body.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task NextLinkKeepsExpandAndCount()
    {
        var (status, body, _) = await Send($"{Warehouses}?$expand=CountryInfo&$count=true&$top=1&$orderby=Id");

        Assert.Equal(HttpStatusCode.OK, status);
        var next = body.GetProperty("nextLink").GetString()!;
        Assert.Contains("$expand=CountryInfo", next);
        Assert.Contains("$count=true", next);

        var (nextStatus, nextBody, _) = await Send(next);
        Assert.Equal(HttpStatusCode.OK, nextStatus);
        Assert.Equal("Germany", Row(nextBody, "B").GetProperty("CountryInfo").GetProperty("CountryName").GetString());
    }

    [Fact]
    public async Task QueryBodyAppliesExpandAndCount()
    {
        var (status, body, _) = await Send(Warehouses, """{"$expand":"CountryInfo","$count":true}""");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(3, body.GetProperty("totalCount").GetInt64());
        Assert.Equal("Netherlands", Row(body, "A").GetProperty("CountryInfo").GetProperty("CountryName").GetString());
    }

    [Theory]
    [InlineData("$top=-1")]
    [InlineData("$skip=-1")]
    public async Task NegativePagingIsRefused(string paging)
    {
        var (status, _, _) = await Send($"{Warehouses}?{paging}");

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task NegativeQueryBodyTopIsRefused()
    {
        var (status, _, _) = await Send(Warehouses, """{"$top":-1}""");

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task ExpandRefusesTargetOutsideTokenScope()
    {
        var (direct, _, _) = await Send($"/api/{Env}/OdataRead/Countries", token: ScopedToken);
        var (status, _, raw) = await Send($"{Warehouses}?$expand=CountryInfo", token: ScopedToken);

        Assert.Equal(HttpStatusCode.Forbidden, direct);
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("Unknown $expand navigation(s): CountryInfo", raw);
        Assert.DoesNotContain("Netherlands", raw);
    }

    [Theory]
    [InlineData("Hidden")]
    [InlineData("OtherEnv")]
    public async Task ExpandRefusesUnavailableTarget(string navigation)
    {
        var (status, _, raw) = await Send($"{Warehouses}?$expand={navigation}");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains($"Unknown $expand navigation(s): {navigation}", raw);
        Assert.DoesNotContain("Netherlands", raw);
    }

    [Theory]
    [InlineData("$filter=Code eq")]
    [InlineData("$orderby=Code,")]
    [InlineData("$filter=1 eq 1")]
    [InlineData("$filter=Code eq 'x' or 1 eq 1")]
    [InlineData("$filter=Id add 1 gt 2")]
    [InlineData("$orderby=1")]
    public async Task MalformedOrUnsupportedODataIsInvalidQuery(string option)
    {
        var (status, _, raw) = await Send($"{Warehouses}?{option}");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("Invalid OData query", raw);
        Assert.DoesNotContain("Columns not allowed", raw);
    }

    [Fact]
    public async Task NavigationPathFilterNamesThePath()
    {
        var (status, _, raw) = await Send($"{Warehouses}?$filter=CountryInfo/CountryName eq 'x'");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("CountryInfo/CountryName", raw);
    }

    [Fact]
    public async Task HiddenColumnFilterIsRefusedByName()
    {
        var (status, _, raw) = await Send($"{Warehouses}?$filter=Secret eq 'x'");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("Columns not allowed: Secret", raw);
    }
}
