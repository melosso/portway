using System.Net;
using System.Text.Json;
using PortwayApi.Tests.Base;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

/// <summary>Verifies OpenAPI document generation end to end; guards the Microsoft.OpenApi upgrade path since Scalar only renders what this endpoint produces</summary>
public class OpenApiDocumentTests : ApiTestBase
{
    [Fact]
    public async Task OpenApiDocument_Generates_And_Parses()
    {
        var response = await _client.GetAsync("/docs/openapi/v1/openapi.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("openapi", out var version));
        Assert.StartsWith("3.2", version.GetString());
        Assert.True(root.TryGetProperty("paths", out var paths));
        Assert.True(paths.ValueKind == JsonValueKind.Object);
        Assert.True(root.TryGetProperty("info", out _));
    }

    // Regression: a QUERY-only endpoint must not be advertised as GET, and generating the document
    // must not mutate the endpoint's methods (which previously injected GET and broke the 405 gate).
    [Fact]
    public async Task QueryOnlyEndpoint_NotRenderedAsGet_AndGetStays405()
    {
        SetAllowedEnvironments("500", "700");

        // Generate the OpenAPI document (runs the document filter over the live endpoint definitions)
        var docResponse = await _client.GetAsync("/docs/openapi/v1/openapi.json");
        Assert.Equal(HttpStatusCode.OK, docResponse.StatusCode);

        using var doc = JsonDocument.Parse(await docResponse.Content.ReadAsStringAsync());
        var paths = doc.RootElement.GetProperty("paths");

        // The QUERY-only endpoint is documented as a native OpenAPI 3.2 query operation, never as GET
        Assert.True(paths.TryGetProperty("/api/{env}/Inventory/StockLevels", out var stockPath),
            "QUERY-only endpoint should be present in the 3.2 document");
        Assert.True(stockPath.TryGetProperty("query", out var queryOp),
            "A QUERY-only endpoint must be documented as a query operation");
        Assert.True(queryOp.TryGetProperty("requestBody", out _),
            "The query operation should document its JSON request body");
        // The author-provided example from the endpoint's Documentation block flows into the success response
        Assert.Contains("SKU-1001", queryOp.GetRawText());
        Assert.False(stockPath.TryGetProperty("get", out _),
            "A QUERY-only endpoint must not be documented as GET");

        // Generating the document must not have enabled GET at runtime
        var getResponse = await _client.GetAsync("/api/500/Inventory/StockLevels");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, getResponse.StatusCode);
    }
}
