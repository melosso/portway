using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Moq;
using PortwayApi.Classes;
using PortwayApi.Classes.OpenApi;
using PortwayApi.Tests.Base;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

/// <summary>
/// Stability and Badges from entity.json reach the OpenAPI document as Scalar extensions
/// </summary>
public sealed class EndpointDocsMetadataTests : ApiTestBase, IDisposable
{
    private static readonly string Root = Path.Combine("endpoints", "Static", "DocMeta");
    private static readonly string ProxyRoot = Path.Combine("endpoints", "Proxy", "DocMetaProxy");
    private static readonly string SqlRoot = Path.Combine("endpoints", "SQL", "DocMetaSql");

    public EndpointDocsMetadataTests()
    {
        Write("Beta", new
        {
            Stability = "Experimental",
            Badges = new object[]
            {
                new { Name = "Preview", Position = "before", Color = "#ff6b35" },
                new { Name = "New" }
            }
        });
        Write("Old", new { Stability = "Stable", Deprecated = true });
        Write("Plain", new { });
        Write("Parent", new
        {
            Deprecated = true,
            Stability = "Stable",
            Documentation = new { Examples = new { GET = new[] { new { marker = "parent-example" } } } }
        });
        Write(Path.Combine("Parent", "Child"), new { Stability = "Experimental" });
        WriteEntity(Path.Combine(ProxyRoot, "Parent"), """{ "Url": "http://localhost:9/p", "Methods": ["GET"], "AllowedEnvironments": ["500"], "Tenancy": { "X-Tenant-Parent": "X-Tenant-Parent" } }""");
        WriteEntity(Path.Combine(ProxyRoot, "Parent", "Child"), """{ "Url": "http://localhost:9/c", "Methods": ["GET"], "AllowedEnvironments": ["500"] }""");
        WriteEntity(Path.Combine(SqlRoot, "Parent"), """{ "DatabaseObjectName": "f", "DatabaseSchema": "dbo", "DatabaseObjectType": "TableValuedFunction", "AllowedColumns": ["a"], "AllowedEnvironments": ["500"], "FunctionParameters": [{ "Name": "parentParam", "SqlType": "int", "Source": "query" }] }""");
        WriteEntity(Path.Combine(SqlRoot, "Parent", "Child"), """{ "DatabaseObjectName": "t", "DatabaseSchema": "dbo", "AllowedColumns": ["a"], "PrimaryKey": "a", "AllowedMethods": ["GET", "DELETE"], "AllowedEnvironments": ["500"] }""");
        Write("Broken", new { Stability = "Deprecated", Badges = new[] { new { Name = "Bad", Color = "red; background:url(x)" } } });
        EndpointHandler.ReloadAllEndpoints();
        SetAllowedEnvironments("500");
    }

    public new void Dispose()
    {
        Directory.Delete(Root, recursive: true);
        Directory.Delete(ProxyRoot, recursive: true);
        Directory.Delete(SqlRoot, recursive: true);
        EndpointHandler.ReloadAllEndpoints();
        base.Dispose();
    }

    private static void Write(string name, object settings)
    {
        var dir = Path.Combine(Root, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "content.json"), "[]");
        var entity = JsonSerializer.SerializeToNode(settings)!.AsObject();
        entity["ContentType"] = "application/json";
        entity["ContentFile"] = "content.json";
        entity["AllowedEnvironments"] = new JsonArray("500");
        File.WriteAllText(Path.Combine(dir, "entity.json"), entity.ToJsonString());
    }

    private static void WriteEntity(string dir, string entity)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "entity.json"), entity);
    }

    private async Task<HttpResponseMessage> Get(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        return await _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<JsonElement> Operation(string name)
    {
        var response = await Get("/docs/openapi.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return document.RootElement.GetProperty("paths").GetProperty($"/api/{{env}}/DocMeta/{name}").GetProperty("get").Clone();
    }

    [Fact]
    public async Task Document_ContainsStabilityAndBadges()
    {
        var operation = await Operation("Beta");

        Assert.Equal("experimental", operation.GetProperty("x-scalar-stability").GetString());
        var badges = operation.GetProperty("x-badges").EnumerateArray().ToList();
        Assert.Equal("Preview", badges[0].GetProperty("name").GetString());
        Assert.Equal("before", badges[0].GetProperty("position").GetString());
        Assert.Equal("#ff6b35", badges[0].GetProperty("color").GetString());
        Assert.Equal("New", badges[1].GetProperty("name").GetString());
        Assert.False(badges[1].TryGetProperty("position", out _));
        Assert.False(badges[1].TryGetProperty("color", out _));
    }

    [Fact]
    public async Task DeprecatedEndpoint_OmitsStability()
    {
        var operation = await Operation("Old");

        Assert.True(operation.GetProperty("deprecated").GetBoolean());
        Assert.False(operation.TryGetProperty("x-scalar-stability", out _));
    }

    [Fact]
    public async Task DeprecatedParent_LeavesNestedEndpointAlone()
    {
        var child = await Operation("Parent/Child");

        Assert.False(child.TryGetProperty("deprecated", out var deprecated) && deprecated.GetBoolean());
        Assert.Equal("experimental", child.GetProperty("x-scalar-stability").GetString());
        Assert.DoesNotContain("parent-example", child.GetRawText());
        Assert.True((await Operation("Parent")).GetProperty("deprecated").GetBoolean());
    }

    [Fact]
    public async Task TenancyAndFunctionParameters_StayOnTheParent()
    {
        var response = await Get("/docs/openapi.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var paths = document.RootElement.GetProperty("paths");

        Assert.Contains("X-Tenant-Parent", paths.GetProperty("/api/{env}/DocMetaProxy/Parent").GetRawText());
        Assert.DoesNotContain("X-Tenant-Parent", paths.GetProperty("/api/{env}/DocMetaProxy/Parent/Child").GetRawText());
        Assert.Contains("parentParam", paths.GetProperty("/api/{env}/DocMetaSql/Parent").GetRawText());
        Assert.DoesNotContain("parentParam", paths.GetProperty("/api/{env}/DocMetaSql/Parent/Child").GetRawText());
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("QUERY")]
    public async Task SqlRead_MalformedKey_ReturnsBadRequest(string method)
    {
        foreach (var key in new[] { "ABC", "", "1 or 1=1" })
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), $"/api/500/DocMetaSql/Parent/Child({Uri.EscapeDataString(key)})");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
            if (method == "QUERY")
                request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

            var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Theory]
    [InlineData("123")]
    [InlineData("'ABC'")]
    [InlineData("guid'3f2a1b4c-0000-4000-8000-000000000000'")]
    [InlineData("3f2a1b4c-0000-4000-8000-000000000000")]
    public async Task SqlRead_DocumentedKeyForms_AreNotRejected(string key)
    {
        var response = await Get($"/api/500/DocMetaSql/Parent/Child({key})");

        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ProxyKey_IsLeftToTheUpstream()
    {
        var response = await Get("/api/500/DocMetaProxy/Parent/Child(Order=1,Item=2)");

        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task IdParameter_DescribesAcceptedKeyForms()
    {
        var response = await Get("/docs/openapi.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var path = document.RootElement.GetProperty("paths").GetProperty("/api/{env}/DocMetaSql/Parent/Child({id})");

        foreach (var verb in new[] { "get", "delete" })
        {
            var id = path.GetProperty(verb).GetProperty("parameters").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "id");
            var description = id.GetProperty("description").GetString()!;
            Assert.Contains("123, 'ABC' or guid'3f2a...'", description);
            Assert.DoesNotContain("OData-style", description);
            Assert.DoesNotContain("Primary key", description);

            var pattern = new System.Text.RegularExpressions.Regex(id.GetProperty("schema").GetProperty("pattern").GetString()!);
            Assert.Matches(pattern, "123");
            Assert.Matches(pattern, "'ABC'");
            Assert.Matches(pattern, "guid'3f2a1b4c-0000-4000-8000-000000000000'");
            Assert.DoesNotMatch(pattern, "ABC");
        }
    }

    [Fact]
    public async Task Markdown_ContainsStabilityAndBadges()
    {
        var markdown = await (await Get("/docs/openapi.md")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains("Stability: experimental", markdown);
        Assert.Contains("Badges: Preview, New", markdown);
    }

    [Fact]
    public async Task UnsetEndpoint_OmitsExtensions()
    {
        var operation = await Operation("Plain");

        Assert.False(operation.TryGetProperty("x-scalar-stability", out _));
        Assert.False(operation.TryGetProperty("x-badges", out _));
    }

    [Theory]
    [InlineData("Stable", null, null, null, true)]
    [InlineData("experimental", "New", "BEFORE", "#abc", true)]
    [InlineData(null, "New", "after", "rgb(1, 2, 3)", true)]
    [InlineData(null, "New", null, "hsla(1, 2%, 3%, 0.5)", true)]
    [InlineData(null, "New", null, "rebeccapurple", true)]
    [InlineData("Deprecated", null, null, null, false)]
    [InlineData("Beta", null, null, null, false)]
    [InlineData(null, "", null, null, false)]
    [InlineData(null, "123456789012345678901234567890123", null, null, false)]
    [InlineData(null, "New", "sideways", null, false)]
    [InlineData(null, "New", null, "#12", false)]
    [InlineData(null, "New", null, "red; background:url(x)", false)]
    [InlineData(null, "New", null, "red\n", false)]
    public void Rules_AcceptOnlyDocumentedValues(string? stability, string? name, string? position, string? color, bool valid)
    {
        var definition = new EndpointDefinition
        {
            Stability = stability,
            Badges = name is null ? null : [new EndpointBadge(name, position, color)]
        };

        Assert.Equal(valid, PortwayApi.Helpers.EndpointMetadataRules.Validate(definition).Count == 0);
    }

    [Fact]
    public async Task ShowBadgesOff_KeepsConfiguredBadges()
    {
        var settings = new Mock<IOptionsMonitor<OpenApiSettings>>();
        settings.Setup(s => s.CurrentValue).Returns(new OpenApiSettings { ShowBadges = false });
        var operation = new OpenApiOperation
        {
            Parameters = [new OpenApiParameter { Name = "$filter" }]
        };
        var document = new OpenApiDocument
        {
            Paths = new OpenApiPaths
            {
                ["/api/{env}/DocMeta/Beta"] = new OpenApiPathItem { Operations = new Dictionary<HttpMethod, OpenApiOperation> { [HttpMethod.Get] = operation } },
                ["/api/{env}/NotConfigured"] = new OpenApiPathItem { Operations = new Dictionary<HttpMethod, OpenApiOperation> { [HttpMethod.Get] = new() } }
            }
        };

        await new ScalarExtensionDocumentFilter(settings.Object).TransformAsync(document, null!, TestContext.Current.CancellationToken);

        var badges = Assert.IsType<JsonArray>(Assert.IsType<JsonNodeExtension>(operation.Extensions!["x-badges"]).Node);
        Assert.Equal(["Preview", "New"], badges.Select(b => b!["name"]!.GetValue<string>()));
    }

    [Fact]
    public async Task InvalidMetadata_KeepsEndpointServing_WithoutTheValues()
    {
        var operation = await Operation("Broken");

        Assert.False(operation.TryGetProperty("x-scalar-stability", out _));
        Assert.False(operation.TryGetProperty("x-badges", out _));
        Assert.Equal(HttpStatusCode.OK, (await Get("/api/500/DocMeta/Broken")).StatusCode);
    }
}
