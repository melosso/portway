using System.Net;
using System.Text.Json;
using PortwayApi.Tests.Base;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

/// <summary>
/// Tag names follow the route, labels come from DisplayName and NamespaceDisplayName, every tag an operation uses is declared
/// </summary>
public class OpenApiTagTests : ApiTestBase
{
    private async Task<JsonDocument> GetDocumentAsync()
    {
        SetAllowedEnvironments("500", "700", "WMS");
        var response = await _client.GetAsync("/docs/openapi/v1/openapi.json", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private static Dictionary<string, JsonElement> Tags(JsonDocument doc) =>
        doc.RootElement.GetProperty("tags").EnumerateArray().ToDictionary(t => t.GetProperty("name").GetString()!, StringComparer.Ordinal);

    private static IEnumerable<(string Path, string Tag)> OperationTags(JsonDocument doc) =>
        from path in doc.RootElement.GetProperty("paths").EnumerateObject()
        from op in path.Value.EnumerateObject()
        where op.Value.ValueKind == JsonValueKind.Object && op.Value.TryGetProperty("tags", out _)
        from tag in op.Value.GetProperty("tags").EnumerateArray()
        select (path.Name, tag.GetString()!);

    private static string? Text(JsonElement tag, string property) =>
        tag.TryGetProperty(property, out var value) ? value.GetString() : null;

    [Fact]
    public async Task NamespacedEndpoint_IsTaggedByItsRoute_AndLabelledByDisplayNames()
    {
        using var doc = await GetDocumentAsync();
        var tags = Tags(doc);

        Assert.All(OperationTags(doc).Where(o => o.Path.StartsWith("/api/{env}/Finance/OutstandingItems")), o => Assert.Equal("Finance/OutstandingItems", o.Tag));

        var leaf = tags["Finance/OutstandingItems"];
        Assert.Equal("Finance", Text(leaf, "parent"));
        Assert.Equal("Outstanding Items", Text(leaf, "summary"));
        Assert.Equal("Servicing", Text(tags["ServiceRequest"], "summary"));
        Assert.Equal("ServiceRequest", Text(tags["ServiceRequest/Requests"], "parent"));
    }

    [Fact]
    public async Task EveryOperationTag_IsDeclared()
    {
        using var doc = await GetDocumentAsync();
        var tags = Tags(doc);

        Assert.All(OperationTags(doc), o => Assert.True(tags.ContainsKey(o.Tag), $"{o.Path} uses undeclared tag {o.Tag}"));
    }

    [Fact]
    public async Task EveryParent_IsADeclaredTag_AndGroupsAreNav()
    {
        using var doc = await GetDocumentAsync();
        var tags = Tags(doc);
        var parents = tags.Values.Select(t => Text(t, "parent")).OfType<string>().ToHashSet(StringComparer.Ordinal);

        Assert.All(parents, p => Assert.True(tags.ContainsKey(p), $"parent {p} is not a declared tag"));
        Assert.All(parents, p => Assert.Equal("nav", Text(tags[p], "kind")));
    }

    [Fact]
    public async Task NestedNamespaceLabel_DoesNotDetachTheTree()
    {
        using var doc = await GetDocumentAsync();
        var tags = Tags(doc);

        Assert.Equal("WMS/Inbound", Text(tags["WMS/Inbound/StagingBins"], "parent"));
        Assert.Equal("WMS", Text(tags["WMS/Inbound"], "parent"));
        Assert.Equal("Inbound Logistics", Text(tags["WMS/Inbound"], "summary"));
    }

    [Fact]
    public async Task FlatFileEndpoint_NestsUnderFiles_WithItsOwnDescription()
    {
        using var doc = await GetDocumentAsync();
        var tags = Tags(doc);

        Assert.All(OperationTags(doc).Where(o => o.Path.StartsWith("/api/{env}/files/CustomerData")), o => Assert.Equal("Files/CustomerData", o.Tag));
        Assert.Equal("Files", Text(tags["Files/CustomerData"], "parent"));
        Assert.Contains("Customer exports", Text(tags["Files/CustomerData"], "description"));
        Assert.False(tags.ContainsKey("CustomerData"), "file TagDescription must not create a tag without operations");
    }
}
