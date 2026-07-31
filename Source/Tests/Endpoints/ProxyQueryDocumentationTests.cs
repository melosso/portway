using PortwayApi.Tests.Base;
using System.Text.Json;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

/// <summary>Portway forwards proxy query strings untouched, so only endpoints declaring SupportsOData may advertise OData parameters</summary>
/// <remarks>
/// Fixtures:
/// - Proxy Account/Accounts : SupportsOData true, fronts an Exact REST service
/// - Proxy Shipments        : no SupportsOData, fronts a plain REST service
/// </remarks>
public class ProxyQueryDocumentationTests : ApiTestBase
{
    private async Task<JsonElement> GetOperationAsync(string path)
    {
        SetAllowedEnvironments("500", "700", "Synergy");

        var response = await _client.GetAsync("/docs/openapi/v1/openapi.json");
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("paths").GetProperty(path).GetProperty("get").Clone();
    }

    private static string[] ParameterNames(JsonElement operation) =>
        operation.TryGetProperty("parameters", out var p)
            ? p.EnumerateArray().Select(x => x.GetProperty("name").GetString()!).ToArray()
            : [];

    // An endpoint declaring OData support advertises the named parameters its upstream understands
    [Fact]
    public async Task ODataProxy_AdvertisesNamedParameters()
    {
        var operation = await GetOperationAsync("/api/{env}/Account/Accounts");

        var names = ParameterNames(operation);
        Assert.Contains("$select", names);
        Assert.Contains("$filter", names);
        Assert.Contains("$top", names);
    }

    // A proxy in front of a plain REST service must not invent OData parameters nobody implements
    [Fact]
    public async Task NonODataProxy_DoesNotInventODataParameters()
    {
        var operation = await GetOperationAsync("/api/{env}/Shipments");

        var names = ParameterNames(operation);
        Assert.DoesNotContain("$select", names);
        Assert.DoesNotContain("$filter", names);
        Assert.DoesNotContain("$top", names);
    }

    // Passthrough is stated as a querystring parameter rather than left unsaid
    [Fact]
    public async Task NonODataProxy_DocumentsPassthrough()
    {
        var operation = await GetOperationAsync("/api/{env}/Shipments");

        var passthrough = operation.GetProperty("parameters").EnumerateArray()
            .Single(p => p.GetProperty("in").GetString() == "querystring");

        Assert.True(passthrough.GetProperty("content").TryGetProperty("application/x-www-form-urlencoded", out _));
        Assert.False(passthrough.TryGetProperty("schema", out _), "querystring parameters carry a content field, not a schema");
    }

    // OpenAPI 3.2 forbids querystring and named query parameters in the same operation
    [Fact]
    public async Task NoOperation_MixesQuerystringWithNamedQueryParameters()
    {
        SetAllowedEnvironments("500", "700", "Synergy", "WMS");

        var response = await _client.GetAsync("/docs/openapi/v1/openapi.json");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var offenders = new List<string>();
        foreach (var path in doc.RootElement.GetProperty("paths").EnumerateObject())
        foreach (var op in path.Value.EnumerateObject())
        {
            if (!op.Value.TryGetProperty("parameters", out var parameters)) continue;

            var locations = parameters.EnumerateArray()
                .Select(p => p.GetProperty("in").GetString())
                .ToArray();

            if (locations.Contains("querystring") && locations.Contains("query"))
            {
                offenders.Add($"{path.Name} {op.Name}");
            }
        }

        Assert.True(offenders.Count == 0, "Operations mixing querystring with query parameters:\n" + string.Join("\n", offenders));
    }
}
