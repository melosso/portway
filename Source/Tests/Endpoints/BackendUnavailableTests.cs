using System.Net;
using System.Text.Json;
using Moq;
using PortwayApi.Classes;
using PortwayApi.Tests.Base;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

public sealed class BackendUnavailableTests : ApiTestBase, IDisposable
{
    private const string Env = "500";
    private static readonly string SqlDir = Path.Combine("endpoints", "SQL", "UnavailableTest");
    private static readonly string ProxyDir = Path.Combine("endpoints", "Proxy", "UnavailableTest");
    private static readonly string WebhookDir = Path.Combine("endpoints", "Webhooks", "UnavailableTest");

    public BackendUnavailableTests()
    {
        SetAllowedEnvironments(Env);
        Write(Path.Combine(SqlDir, "Items"), new { DatabaseObjectName = "Items", AllowedColumns = new[] { "Id" }, AllowedEnvironments = new[] { Env } });
        Write(Path.Combine(SqlDir, "Generated"), new
        {
            DatabaseObjectName = "GenerateItems",
            DatabaseObjectType = "TableValuedFunction",
            FunctionParameters = new[] { new { Name = "Since", SqlType = "date", Source = "Query", Required = false } },
            AllowedColumns = new[] { "Id" },
            AllowedEnvironments = new[] { Env }
        });
        Write(Path.Combine(ProxyDir, "Down"), new { Url = "http://127.0.0.1:1/items", Methods = new[] { "GET", "POST" }, AllowedEnvironments = new[] { Env } });
        Write(Path.Combine(ProxyDir, "Combined"), new
        {
            Type = "Composite",
            Url = "http://127.0.0.1:1/items",
            Methods = new[] { "POST" },
            AllowedEnvironments = new[] { Env },
            CompositeConfig = new { Name = "Combined", Steps = new[] { new { Name = "First", Endpoint = "UnavailableTest/Down", Method = "POST" } } }
        });
        Write(Path.Combine(WebhookDir, "Hooks"), new { DatabaseObjectName = "WebhookData", AllowedColumns = new[] { "hook" }, AllowedEnvironments = new[] { Env } });
        EndpointHandler.ReloadAllEndpoints();
        _mockODataToSqlConverter
            .Setup(c => c.ConvertToSQL(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<PortwayApi.Services.Providers.SqlProviderType>()))
            .Returns(("SELECT * FROM [dbo].[ODataDummy]", new Dictionary<string, object>()));
        AddAuthorizationHeader();
    }

    protected override string ConnectionStringFor(string environment) =>
        "Server=127.0.0.1,1;Database=unavailable;User ID=u;Password=unavailable-pw;Connect Timeout=2;TrustServerCertificate=True";

    public new void Dispose()
    {
        Directory.Delete(SqlDir, recursive: true);
        Directory.Delete(ProxyDir, recursive: true);
        Directory.Delete(WebhookDir, recursive: true);
        EndpointHandler.ReloadAllEndpoints();
        base.Dispose();
    }

    private static void Write(string dir, object entity)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "entity.json"), JsonSerializer.Serialize(entity));
    }

    [Theory]
    [InlineData("UnavailableTest/Items")]
    [InlineData("UnavailableTest/Generated")]
    [InlineData("UnavailableTest/Down")]
    public async Task UnreachableBackend_Returns503WithoutDetail(string endpoint)
    {
        var response = await _client.GetAsync($"/api/{Env}/{endpoint}", TestContext.Current.CancellationToken);
        await AssertUnavailable(response);
    }

    [Fact]
    public async Task UnreachableCompositeStep_Returns503WithoutDetail()
    {
        var response = await _client.PostAsync($"/api/{Env}/UnavailableTest/Combined",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);
        await AssertUnavailable(response);
    }

    [Fact]
    public async Task UnreachableWebhookDatabase_Returns503WithoutDetail()
    {
        var response = await _client.PostAsync($"/api/{Env}/UnavailableTest/Hooks/hook",
            new StringContent("{\"a\":1}", System.Text.Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);
        await AssertUnavailable(response);
    }

    private static async Task AssertUnavailable(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.True(response.StatusCode == HttpStatusCode.ServiceUnavailable, $"{(int)response.StatusCode} {body}");
        Assert.DoesNotContain("127.0.0.1", body);
        Assert.DoesNotContain("unavailable-pw", body);
        Assert.DoesNotContain("refused", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(JsonValueKind.String, JsonDocument.Parse(body).RootElement.GetProperty("error").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(JsonDocument.Parse(body).RootElement.GetProperty("traceId").GetString()));
    }
}
