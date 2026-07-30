using PortwayApi.Tests.Base;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

/// <summary>Integration tests for MERGE, the OData spelling of a partial update</summary>
/// <remarks>
/// Fixtures:
/// - SQL WMS/Bins       : AllowedMethods include MERGE, env WMS (table writes against the SQLite demo db)
/// - SQL WMS/Warehouses : AllowedMethods ["GET"], env WMS
/// - Proxy Account/Accounts : Methods GET/POST/PUT/DELETE (no MERGE), env 500
/// </remarks>
public class MergeEndpointTests : ApiTestBase
{
    private static HttpRequestMessage Merge(string url, string json = "{}") =>
        new(new HttpMethod("MERGE"), url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    // A SQL endpoint declaring MERGE routes and passes method gating, so the verb is real and not just documented
    [Fact]
    public async Task Merge_SqlEndpointAllowingMerge_IsAccepted()
    {
        SetAllowedEnvironments("WMS");

        var response = await _client.SendAsync(Merge("/api/WMS/WMS/Bins", "{\"Id\":1,\"Zone\":\"A\"}"));

        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // A GET-only SQL endpoint rejects MERGE with 405, gated on its declared methods
    [Fact]
    public async Task Merge_SqlEndpointWithoutMerge_Returns405()
    {
        SetAllowedEnvironments("WMS");

        var response = await _client.SendAsync(Merge("/api/WMS/WMS/Warehouses", "{\"Id\":1}"));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    // A proxy endpoint whose Methods omit MERGE rejects it before any upstream call
    [Fact]
    public async Task Merge_ProxyEndpointWithoutMerge_Returns405()
    {
        SetAllowedEnvironments("500", "700");

        var response = await _client.SendAsync(Merge("/api/500/Account/Accounts"));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    // MERGE is documented under OpenAPI 3.2 additionalOperations, not disguised as PATCH
    [Fact]
    public async Task Merge_IsDocumentedAsAdditionalOperation()
    {
        SetAllowedEnvironments("WMS");

        var response = await _client.GetAsync("/docs/openapi/v1/openapi.json");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var pathItem = doc.RootElement.GetProperty("paths").GetProperty("/api/{env}/WMS/Bins");
        var merge = pathItem.GetProperty("additionalOperations").GetProperty("MERGE");

        Assert.True(merge.TryGetProperty("responses", out _));
        Assert.False(pathItem.TryGetProperty("merge", out _), "MERGE must not be emitted as a fixed path item field");
    }
}
