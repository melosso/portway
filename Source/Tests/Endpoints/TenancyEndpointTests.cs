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
/// Tenant headers through the HTTP pipeline for SQL, proxy and file endpoints
/// </summary>
public sealed class TenancyEndpointTests : ApiTestBase, IDisposable
{
    private const string Env = "TEN";
    private const string Orders = $"/api/{Env}/TenantTest/Orders";
    private const string Ledger = $"/api/{Env}/TenantTest/Ledger";
    private const string Docs = $"/api/{Env}/files/TenantDocs";
    private const int UpstreamPort = 8021;

    private static readonly string[] EndpointDirs =
    [
        Path.Combine("endpoints", "SQL", "TenantTest"),
        Path.Combine("endpoints", "Proxy", "TenantTest"),
        Path.Combine("endpoints", "Files", "TenantDocs"),
    ];

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"portway_tenancy_{Guid.NewGuid():N}.db");
    private readonly UpstreamCapture _upstream = new(UpstreamPort);

    public TenancyEndpointTests()
    {
        using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
        {
            connection.Execute("""
                CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CompanyId TEXT NOT NULL, Total INTEGER NOT NULL);
                INSERT INTO Orders VALUES (1, 'ACME', 10), (2, 'ACME', 20), (3, 'GLOBEX', 30);
                """);
        }

        WriteEndpoint(EndpointDirs[0], "Orders", new
        {
            DatabaseObjectName = "Orders",
            DatabaseObjectType = "Table",
            AllowedEnvironments = new[] { Env },
            AllowedMethods = new[] { "GET", "POST", "PUT", "DELETE" },
            WriteMode = "Table",
            PrimaryKey = "Id",
            AllowedColumns = new[] { "Id", "Total" },
            Tenancy = new Dictionary<string, string> { ["X-Company-Id"] = "CompanyId" },
        });
        WriteEndpoint(EndpointDirs[1], "Ledger", new
        {
            Url = $"http://localhost:{UpstreamPort}/ledger",
            Methods = new[] { "GET" },
            AllowedEnvironments = new[] { Env },
            Tenancy = new Dictionary<string, string> { ["X-Company-Id"] = "Administratie" },
        });
        WriteEndpoint(EndpointDirs[2], null, new
        {
            StorageType = "Local",
            BaseDirectory = "tenant-docs/{X-Company-Id}",
            AllowedEnvironments = new[] { Env },
            Tenancy = new Dictionary<string, string> { ["X-Company-Id"] = "" },
        });
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

        Token("acme-token", """{"X-Company-Id":["ACME"]}""");
        Token("multi-token", """{"X-Company-Id":["ACME","GLOBEX"]}""");
    }

    public new void Dispose()
    {
        foreach (var dir in EndpointDirs.Where(Directory.Exists))
            Directory.Delete(dir, recursive: true);
        EndpointHandler.ReloadAllEndpoints();
        _upstream.Dispose();
        base.Dispose();
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    private static void WriteEndpoint(string dir, string? name, object content)
    {
        var target = name is null ? dir : Path.Combine(dir, name);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "entity.json"), JsonSerializer.Serialize(content));
    }

    private void Token(string token, string tenants) =>
        _mockTokenService.Setup(s => s.GetTokenDetailsByTokenAsync(token)).ReturnsAsync(new AuthToken
        {
            Username = token,
            TokenHash = "hash",
            TokenSalt = "salt",
            AllowedScopes = "*",
            AllowedEnvironments = "*",
            AllowedTenants = tenants,
        });

    private async Task<HttpResponseMessage> Send(HttpMethod method, string url, string token, string? company = null, HttpContent? body = null)
    {
        using var request = new HttpRequestMessage(method, url) { Content = body };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (company is not null)
            request.Headers.Add("X-Company-Id", company);
        return await _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<List<int>> OrderIds(string token, string? company = null, string query = "")
    {
        var response = await Send(HttpMethod.Get, Orders + query, token, company);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return json.RootElement.GetProperty("value").EnumerateArray().Select(r => r.GetProperty("Id").GetInt32()).Order().ToList();
    }

    private string CompanyOf(int id)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        return connection.ExecuteScalar<string?>("SELECT CompanyId FROM Orders WHERE Id = @id", new { id }) ?? "";
    }

    private static StringContent Json(object value) => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    [Fact]
    public async Task TokenWithoutTenantsRefused() =>
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, Orders, "test-token", "ACME")).StatusCode);

    [Fact]
    public async Task SingleTenantNeedsNoHeader() =>
        Assert.Equal([1, 2], await OrderIds("acme-token"));

    [Fact]
    public async Task HeaderSelectsTenant() =>
        Assert.Equal([3], await OrderIds("multi-token", "GLOBEX"));

    [Fact]
    public async Task MultiTenantNeedsHeader() =>
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Get, Orders, "multi-token")).StatusCode);

    [Fact]
    public async Task HeaderCannotGrantTenant() =>
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, Orders, "acme-token", "GLOBEX")).StatusCode);

    [Fact]
    public async Task FilterCannotWiden() =>
        Assert.Equal([1, 2], await OrderIds("acme-token", query: "?$filter=Id eq 3 or Total gt 0"));

    [Fact]
    public async Task CountIsConfined()
    {
        var response = await Send(HttpMethod.Get, Orders + "?$count=true", "acme-token");

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, json.RootElement.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task OtherTenantRowIsNotFound() =>
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, Orders + "/3", "acme-token")).StatusCode);

    [Fact]
    public async Task InsertIsStamped()
    {
        var response = await Send(HttpMethod.Post, Orders, "multi-token", "GLOBEX", Json(new { Id = 10, Total = 5 }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("GLOBEX", CompanyOf(10));
    }

    [Fact]
    public async Task UpdateOtherTenantNotFound()
    {
        var response = await Send(HttpMethod.Put, Orders, "acme-token", body: Json(new { Id = 3, Total = 99 }));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        Assert.Equal(30, connection.ExecuteScalar<int>("SELECT Total FROM Orders WHERE Id = 3"));
    }

    [Fact]
    public async Task DeleteOtherTenantNotFound()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Delete, Orders + "/3", "acme-token")).StatusCode);
        Assert.Equal("GLOBEX", CompanyOf(3));
    }

    [Fact]
    public async Task ProxySendsTenantUnderUpstreamName()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Ledger);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "multi-token");
        request.Headers.Add("X-Company-Id", "GLOBEX");
        request.Headers.Add("Administratie", "ACME");

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("GLOBEX", _upstream.Headers["Administratie"]);
        Assert.False(_upstream.Headers.ContainsKey("X-Company-Id"));
    }

    [Fact]
    public async Task ProxyRefusesForeignTenant() =>
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, Ledger, "acme-token", "GLOBEX")).StatusCode);

    [Fact]
    public async Task FilesStayInTenantFolder()
    {
        var form = new MultipartFormDataContent { { new ByteArrayContent("acme"u8.ToArray()), "file", "report.csv" } };
        var upload = await Send(HttpMethod.Post, Docs, "acme-token", body: form);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        using var created = JsonDocument.Parse(await upload.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var fileId = created.RootElement.GetProperty("fileId").GetString();

        var ownDownload = await Send(HttpMethod.Get, $"{Docs}/{fileId}", "multi-token", "ACME");
        var foreignDownload = await Send(HttpMethod.Get, $"{Docs}/{fileId}", "multi-token", "GLOBEX");
        var foreignList = await Send(HttpMethod.Get, $"{Docs}/list", "multi-token", "GLOBEX");
        var foreignDelete = await Send(HttpMethod.Delete, $"{Docs}/{fileId}", "multi-token", "GLOBEX");

        Assert.Equal(HttpStatusCode.OK, ownDownload.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignDownload.StatusCode);
        Assert.DoesNotContain(fileId!, await foreignList.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.NotFound, foreignDelete.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Delete, $"{Docs}/{fileId}", "acme-token")).StatusCode);
    }

    [Fact]
    public async Task DocumentListsTenantHeader()
    {
        var response = await _client.GetAsync("/docs/openapi/v1/openapi.json", TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var paths = json.RootElement.GetProperty("paths");
        foreach (var path in new[] { "/api/{env}/TenantTest/Orders", "/api/{env}/TenantTest/Ledger" })
        {
            var parameters = paths.GetProperty(path).GetProperty("get").GetProperty("parameters").EnumerateArray();
            Assert.Contains(parameters, p => p.GetProperty("name").GetString() == "X-Company-Id" && p.GetProperty("in").GetString() == "header");
        }
    }

    [Fact]
    public async Task TenantColumnReadOnlyInRequestBody()
    {
        var body = new Microsoft.OpenApi.OpenApiSchema
        {
            Type = Microsoft.OpenApi.JsonSchemaType.Object,
            Properties = new Dictionary<string, Microsoft.OpenApi.IOpenApiSchema>
            {
                ["companyid"] = new Microsoft.OpenApi.OpenApiSchema { Type = Microsoft.OpenApi.JsonSchemaType.String },
                ["Total"] = new Microsoft.OpenApi.OpenApiSchema { Type = Microsoft.OpenApi.JsonSchemaType.Integer },
            }
        };
        var document = new Microsoft.OpenApi.OpenApiDocument
        {
            Paths = new Microsoft.OpenApi.OpenApiPaths
            {
                ["/api/{env}/TenantTest/Orders"] = new Microsoft.OpenApi.OpenApiPathItem
                {
                    Operations = new Dictionary<HttpMethod, Microsoft.OpenApi.OpenApiOperation>
                    {
                        [HttpMethod.Post] = new()
                        {
                            RequestBody = new Microsoft.OpenApi.OpenApiRequestBody
                            {
                                Content = new Dictionary<string, Microsoft.OpenApi.IOpenApiMediaType> { ["application/json"] = new Microsoft.OpenApi.OpenApiMediaType { Schema = body } }
                            }
                        }
                    }
                }
            }
        };

        await new PortwayApi.Classes.OpenApi.TenancyDocumentFilter().TransformAsync(document, null!, TestContext.Current.CancellationToken);

        Assert.True(((Microsoft.OpenApi.OpenApiSchema)body.Properties["companyid"]).ReadOnly);
        Assert.False(((Microsoft.OpenApi.OpenApiSchema)body.Properties["Total"]).ReadOnly);
    }
}
