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
        Assert.StartsWith("3.", version.GetString());
        Assert.True(root.TryGetProperty("paths", out var paths));
        Assert.True(paths.ValueKind == JsonValueKind.Object);
        Assert.True(root.TryGetProperty("info", out _));
    }
}
