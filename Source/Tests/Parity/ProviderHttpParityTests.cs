namespace PortwayApi.Tests.Parity;

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Moq;
using PortwayApi.Classes;
using PortwayApi.Helpers;
using PortwayApi.Services.Providers;
using PortwayApi.Tests.Base;
using Xunit;

/// <summary>
/// Record key and write scenarios sent over HTTP to every provider's real database
/// </summary>
[Trait("Category", "Parity")]
public abstract class ProviderHttpParityTests<TFixture> : ApiTestBase, IClassFixture<TFixture>, IDisposable
    where TFixture : ParityDatabaseFixture
{
    private const string Env = "PAR";
    private const string GuidKey = ParityDatabaseFixture.GuidKey;

    private readonly string _namespace;
    private readonly string _endpointDir;
    private readonly TFixture _fixture;

    protected virtual HttpStatusCode NonGuidKeyOnGuidColumnStatus => HttpStatusCode.BadRequest;

    // sqlite never converts text keys
    protected virtual bool KeyMismatchIsAnError => true;

    protected ProviderHttpParityTests(TFixture fixture) : base(connectTimeoutSeconds: 15)
    {
        _fixture = fixture;
        _namespace = $"Http{fixture.ProviderType}";
        _endpointDir = Path.Combine("endpoints", "SQL", _namespace);

        var schema = fixture.QualifiedGuidKeysTable.Split('.')[0];
        WriteEndpoint("Native", TableEndpoint(fixture.QualifiedGuidKeysTable, "Id", "Name"));
        WriteEndpoint("Text", TableEndpoint(fixture.QualifiedTextKeysTable, "Id", "Name"));
        WriteEndpoint("Int", TableEndpoint(fixture.QualifiedProductsTable, "Id", "Name", "Price"));
        var readOnly = TableEndpoint(fixture.QualifiedProductsTable, "Id", "Name");
        readOnly["AllowedMethods"] = new[] { "GET" };
        WriteEndpoint("IntReadOnly", readOnly);

        var procedure = TableEndpoint(fixture.QualifiedGuidKeysTable, "Id", "Name");
        procedure.Remove("WriteMode");
        procedure["Procedure"] = $"{schema}.{fixture.GuidKeyProcedureName}";
        WriteEndpoint("Proc", procedure);

        var function = TableEndpoint($"{schema}.{(fixture.TvfName.Length > 0 ? fixture.TvfName : "NoFunction")}", "Id", "Name", "Price");
        function["DatabaseObjectType"] = "TableValuedFunction";
        function["AllowedMethods"] = new[] { "GET" };
        function.Remove("WriteMode");
        function["FunctionParameters"] = new[] { new { Name = "MinPrice", SqlType = "decimal", Source = "Path", Position = 1, Required = true } };
        WriteEndpoint("Tvf", function);
        EndpointHandler.ReloadAllEndpoints();

        SetAllowedEnvironments(Env);
        _mockEnvironmentSettingsProvider
            .Setup(p => p.LoadEnvironmentOrThrowAsync(It.IsAny<string>()))
            .ReturnsAsync((fixture.ConnectionString, "localhost", new Dictionary<string, string>()));

        var converter = new ODataToSqlConverter([new MsSqlProvider(), new PostgreSqlProvider(), new MySqlProvider(), new SqliteProvider()]);
        _mockODataToSqlConverter
            .Setup(c => c.ConvertToSQL(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<SqlProviderType>()))
            .Returns((string e, Dictionary<string, string> o, SqlProviderType p) => converter.ConvertToSQL(e, o, p));
        _mockODataToSqlConverter
            .Setup(c => c.ConvertToSQL(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<SqlProviderType>(), It.IsAny<IReadOnlyList<EndpointRelationship>?>()))
            .Returns((string e, Dictionary<string, string> o, SqlProviderType p, IReadOnlyList<EndpointRelationship>? r) => converter.ConvertToSQL(e, o, p, r));
        _mockODataToSqlConverter
            .Setup(c => c.ConvertToSQL(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<SqlProviderType>()))
            .Returns((string e, Dictionary<string, string> o, SqlProviderType p) => converter.ConvertToSQL(e, o, p));
        _mockODataToSqlConverter
            .Setup(c => c.ConvertToSQL(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<SqlProviderType>(), It.IsAny<IReadOnlyList<EndpointRelationship>?>(), It.IsAny<IReadOnlyList<TenantPredicate>>()))
            .Returns((string e, Dictionary<string, string> o, SqlProviderType p, IReadOnlyList<EndpointRelationship>? r, IReadOnlyList<TenantPredicate> t) => converter.ConvertToSQL(e, o, p, r, t));
    }

    private static Dictionary<string, object?> TableEndpoint(string qualifiedTable, params string[] columns)
    {
        var parts = qualifiedTable.Split('.');
        return new Dictionary<string, object?>
        {
            ["DatabaseSchema"] = parts[0],
            ["DatabaseObjectName"] = parts[1],
            ["DatabaseObjectType"] = "Table",
            ["AllowedEnvironments"] = new[] { Env },
            ["AllowedMethods"] = new[] { "GET", "POST", "PUT", "PATCH", "DELETE" },
            ["WriteMode"] = "Table",
            ["PrimaryKey"] = "Id",
            ["AllowedColumns"] = columns
        };
    }

    private void WriteEndpoint(string name, Dictionary<string, object?> definition)
    {
        var target = Path.Combine(_endpointDir, name);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "entity.json"), JsonSerializer.Serialize(definition));
    }

    public new void Dispose()
    {
        Directory.Delete(_endpointDir, recursive: true);
        EndpointHandler.ReloadAllEndpoints();
        base.Dispose();
    }

    protected string Url(string endpoint, string suffix = "") => $"/api/{Env}/{_namespace}/{endpoint}{suffix}";

    protected async Task<(HttpStatusCode Status, string Body)> Send(HttpMethod method, string url, string? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        if (body is not null)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [DockerFact]
    public async Task GuidKey_EveryDocumentedFormReturnsTheRecord()
    {
        foreach (var endpoint in new[] { "Native", "Text" })
        {
            foreach (var form in new[] { $"({GuidKey})", $"(guid'{GuidKey}')", $"('{GuidKey}')", $"/{GuidKey}" })
            {
                var (status, body) = await Send(HttpMethod.Get, Url(endpoint, form));

                Assert.Equal(HttpStatusCode.OK, status);
                Assert.Contains("Kept", body);
            }
        }
    }

    [DockerFact]
    public async Task UnknownGuidKey_ReturnsNotFound()
    {
        foreach (var endpoint in new[] { "Native", "Text" })
            Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, Url(endpoint, "(00000000-1111-4222-8333-444455556666)"))).Status);
    }

    [DockerFact]
    public async Task GuidKey_CreateUpdateDeleteRoundtrip()
    {
        foreach (var endpoint in new[] { "Native", "Text" })
        {
            var key = Guid.NewGuid().ToString();

            Assert.Equal(HttpStatusCode.Created, (await Send(HttpMethod.Post, Url(endpoint), $$"""{ "Id": "{{key}}", "Name": "New" }""")).Status);
            Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Patch, Url(endpoint), $$"""{ "Id": "{{key}}", "Name": "Changed" }""")).Status);
            Assert.Contains("Changed", (await Send(HttpMethod.Get, Url(endpoint, $"({key})?$select=Name"))).Body);
            Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Delete, Url(endpoint, $"(guid'{key}')"))).Status);
            Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, Url(endpoint, $"({key})"))).Status);
        }
    }

    [DockerFact]
    public async Task DuplicateKey_ReturnsConflict_WithoutTheValue()
    {
        var (status, body) = await Send(HttpMethod.Post, Url("Native"), $$"""{ "Id": "{{GuidKey}}", "Name": "Again" }""");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("Record already exists", body);
        Assert.DoesNotContain(GuidKey, body);
    }

    [DockerFact]
    public async Task NonGuidKey_OnGuidColumn_ReturnsExpectedStatus() =>
        Assert.Equal(NonGuidKeyOnGuidColumnStatus, (await Send(HttpMethod.Get, Url("Native", "('abc')"))).Status);

    [DockerFact]
    public async Task NonNumericValue_OnNumericColumn_ReturnsBadRequest()
    {
        const string payload = """{ "Id": "abc", "Name": "Bad", "Price": 1 }""";

        foreach (var method in KeyMismatchIsAnError ? new[] { HttpMethod.Post, HttpMethod.Patch } : [HttpMethod.Post])
        {
            var (status, body) = await Send(method, Url("Int"), payload);

            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.Contains("Invalid column value", body);
        }

        var (deleteStatus, deleteBody) = await Send(HttpMethod.Delete, Url("Int", "('abc')"));
        if (!KeyMismatchIsAnError)
        {
            Assert.Equal(HttpStatusCode.NotFound, deleteStatus);
            return;
        }

        Assert.Equal(HttpStatusCode.BadRequest, deleteStatus);
        Assert.Contains("Invalid record ID", deleteBody);
    }

    [DockerFact]
    public async Task TextKey_OnNumericColumn_ReturnsNoRecord()
    {
        foreach (var form in new[] { "('3abc')", $"({GuidKey})" })
            Assert.NotEqual(HttpStatusCode.OK, (await Send(HttpMethod.Get, Url("Int", form))).Status);

        Assert.Contains("Bird Seed", (await Send(HttpMethod.Get, Url("Int", "(3)"))).Body);
    }

    [DockerFact]
    public async Task NumericKey_UpdatesAndDeletes()
    {
        const string payload = """{ "Id": 9001, "Name": "Temp", "Price": 1 }""";

        Assert.Equal(HttpStatusCode.Created, (await Send(HttpMethod.Post, Url("Int"), payload)).Status);
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Patch, Url("Int"), """{ "Id": 9001, "Name": "Changed" }""")).Status);
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Delete, Url("Int", "(9001)"))).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, Url("Int", "(9001)"))).Status);
    }

    [DockerFact]
    public async Task Write_InvalidatesCachedRead()
    {
        const string payload = """{ "Id": 9002, "Name": "Cached", "Price": 1 }""";
        Assert.Equal(HttpStatusCode.Created, (await Send(HttpMethod.Post, Url("Int"), payload)).Status);
        Assert.Contains("Cached", (await Send(HttpMethod.Get, Url("Int", "(9002)"))).Body);

        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Patch, Url("Int"), """{ "Id": 9002, "Name": "Renamed" }""")).Status);
        Assert.Contains("Renamed", (await Send(HttpMethod.Get, Url("Int", "(9002)"))).Body);

        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Delete, Url("Int", "(9002)"))).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, Url("Int", "(9002)"))).Status);
    }

    [DockerFact]
    public async Task Filter_OnColumnOutsideAllowedColumns_IsRefused()
    {
        Assert.NotEqual(HttpStatusCode.OK, (await Send(HttpMethod.Get, Url("Int", "?$filter=ReleasedAt eq '2023-03-01'"))).Status);
        Assert.NotEqual(HttpStatusCode.OK, (await Send(HttpMethod.Get, Url("Int", "?$orderby=ReleasedAt"))).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Get, Url("Int", "?$filter=Name ne \"'\" and ReleasedAt ne null and Name ne \"'\""))).Status);

        if (_fixture.Provider.SupportsTvf)
            Assert.NotEqual(HttpStatusCode.OK, (await Send(HttpMethod.Get, Url("Tvf", "/30?$filter=Id eq 4 and CategoryId eq 20"))).Status);
    }

    [DockerFact]
    public async Task RefusedWrite_KeepsCachedRead()
    {
        Assert.Equal(HttpStatusCode.Created, (await Send(HttpMethod.Post, Url("Int"), """{ "Id": 9004, "Name": "Before", "Price": 1 }""")).Status);
        Assert.Contains("Before", (await Send(HttpMethod.Get, Url("IntReadOnly", "(9004)"))).Body);

        await using (var connection = _fixture.Provider.CreateConnection(_fixture.ConnectionString))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            var update = SqlTableWriteBuilder.BuildUpdate(_fixture.Provider, _fixture.QualifiedProductsTable, "Id", 9004, new Dictionary<string, object?> { ["Name"] = "After" });
            await Dapper.SqlMapper.ExecuteAsync(connection, update.Sql, update.Parameters);
        }

        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await Send(HttpMethod.Delete, Url("IntReadOnly", "(9004)"))).Status);
        Assert.Contains("Before", (await Send(HttpMethod.Get, Url("IntReadOnly", "(9004)"))).Body);

        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Delete, Url("Int", "(9004)"))).Status);
    }

    [DockerFact]
    public async Task Tvf_NextLink_ReturnsTheNextPage()
    {
        if (!_fixture.Provider.SupportsTvf)
            return;

        var (status, body) = await Send(HttpMethod.Get, Url("Tvf", "/30?$top=1&$orderby=Id"));
        Assert.Equal(HttpStatusCode.OK, status);

        var nextLink = JsonDocument.Parse(body).RootElement.GetProperty("nextLink").GetString()!;
        Assert.Contains("/30?", nextLink);
        Assert.DoesNotContain("$select", nextLink);

        var (nextStatus, nextBody) = await Send(HttpMethod.Get, nextLink);
        Assert.Equal(HttpStatusCode.OK, nextStatus);
        Assert.Contains("Giant Magnet", nextBody);
    }

    [DockerFact]
    public async Task Tvf_PathFilterAndSelect()
    {
        if (!_fixture.Provider.SupportsTvf)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Get, Url("Tvf", "/30"))).Status);
            return;
        }

        var (status, body) = await Send(HttpMethod.Get, Url("Tvf", "/30"));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(3, JsonDocument.Parse(body).RootElement.GetProperty("value").GetArrayLength());

        var filtered = await Send(HttpMethod.Get, Url("Tvf", "/30?$filter=Id eq 4"));
        Assert.Equal(HttpStatusCode.OK, filtered.Status);
        Assert.Contains("Giant Magnet", filtered.Body);
        Assert.DoesNotContain("Tornado Kit", filtered.Body);

        var selected = await Send(HttpMethod.Get, Url("Tvf", "/30?$select=Name"));
        Assert.Equal(HttpStatusCode.OK, selected.Status);
        Assert.Contains("Giant Magnet", selected.Body);
        Assert.DoesNotContain("Price", selected.Body);

        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Get, Url("Tvf", "/30?$select=ReleasedAt"))).Status);
    }

    [DockerFact]
    public async Task Procedure_Delete_SendsOnlyTheKey()
    {
        if (_fixture.Provider.SupportsProcedures)
            Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Delete, Url("Proc", $"({GuidKey})"))).Status);
    }

    [DockerFact]
    public async Task Procedure_Writes_BindGuidValues()
    {
        if (!_fixture.Provider.SupportsProcedures)
            return;

        var payload = $$"""{ "Id": "{{GuidKey}}", "Name": "Value" }""";

        Assert.Equal(HttpStatusCode.Created, (await Send(HttpMethod.Post, Url("Proc"), payload)).Status);
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Put, Url("Proc"), payload)).Status);
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Patch, Url("Proc"), payload)).Status);
    }
}
