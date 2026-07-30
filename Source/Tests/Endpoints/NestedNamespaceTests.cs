using Moq;
using PortwayApi.Classes;
using PortwayApi.Services.Providers;
using PortwayApi.Tests.Base;
using System.Net;
using System.Text.Json;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

/// <summary>Namespaces nested more than one level deep, which the loader has always produced but routing could not reach</summary>
/// <remarks>Fixture: SQL WMS/Inbound/StagingBins, env WMS, backed by the committed SQLite demo database</remarks>
public class NestedNamespaceTests : ApiTestBase
{
    /// <summary>Only the OData translation is stubbed; routing, handler, driver and envelope remain intact</summary>
    private void StubODataTranslation(string sql)
    {
        _mockODataToSqlConverter
            .Setup(c => c.ConvertToSQL(
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<SqlProviderType>(),
                It.IsAny<IReadOnlyList<EndpointRelationship>?>()))
            .Returns((sql, new Dictionary<string, object>()));
    }

    [Fact]
    public async Task NestedNamespace_Routes()
    {
        SetAllowedEnvironments("WMS");
        StubODataTranslation("SELECT Id, Code, Zone, CapacityUnits, IsActive FROM Bins LIMIT 10");

        var response = await _client.GetAsync("/api/WMS/WMS/Inbound/StagingBins");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.TryGetProperty("value", out _));
    }

    // The shallower namespace must still resolve, so the longest-first probe cannot shadow it
    [Fact]
    public async Task SingleLevelNamespace_StillRoutes()
    {
        SetAllowedEnvironments("WMS");
        StubODataTranslation("SELECT Id, Code, Name FROM Warehouses LIMIT 10");

        var response = await _client.GetAsync("/api/WMS/WMS/Warehouses");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // An id after a nested endpoint is still read as an id, not as another namespace segment
    [Fact]
    public async Task NestedNamespace_ResolvesIdSegment()
    {
        SetAllowedEnvironments("WMS");
        StubODataTranslation("SELECT Id, Code, Zone, CapacityUnits, IsActive FROM Bins LIMIT 10");

        var response = await _client.GetAsync("/api/WMS/WMS/Inbound/StagingBins/1");

        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    // The nested namespace becomes a tag tree, the OpenAPI 3.2 feature it exists to demonstrate
    [Fact]
    public async Task NestedNamespace_RendersAsTagHierarchy()
    {
        SetAllowedEnvironments("WMS");

        var response = await _client.GetAsync("/docs/openapi/v1/openapi.json");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var tags = doc.RootElement.GetProperty("tags").EnumerateArray().ToList();

        var child = tags.Single(t => t.GetProperty("name").GetString() == "WMS/Inbound");
        Assert.Equal("WMS", child.GetProperty("parent").GetString());
        Assert.Equal("nav", child.GetProperty("kind").GetString());

        Assert.Contains(tags, t => t.GetProperty("name").GetString() == "WMS");
    }
}
