using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
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
        SetAllowedEnvironments("500", "700", "WMS", "Synergy");
        var response = await _client.GetAsync("/docs/openapi.json", TestContext.Current.CancellationToken);
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
        Assert.Equal("ns:Finance", Text(leaf, "parent"));
        Assert.Equal("Outstanding Items", Text(leaf, "summary"));
        Assert.Equal("ns:Service", Text(tags["Service/Requests"], "parent"));
        Assert.Equal("Inbound Bins", Text(tags["WMS/InboundBins"], "summary"));
    }

    [Fact]
    public async Task EveryOperationTag_IsDeclared()
    {
        using var doc = await GetDocumentAsync();
        var tags = Tags(doc);

        Assert.All(OperationTags(doc), o => Assert.True(tags.ContainsKey(o.Tag), $"{o.Path} uses undeclared tag {o.Tag}"));
    }

    [Fact]
    public async Task EveryParent_IsADeclaredTag_AndOnlyGroupsAreNav()
    {
        using var doc = await GetDocumentAsync();
        var tags = Tags(doc);
        var parents = tags.Values.Select(t => Text(t, "parent")).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var used = OperationTags(doc).Select(o => o.Tag).ToHashSet(StringComparer.Ordinal);

        Assert.All(parents, p => Assert.True(tags.ContainsKey(p), $"parent {p} is not a declared tag"));
        Assert.All(parents, p => Assert.Equal("nav", Text(tags[p], "kind")));
        Assert.All(used, t => Assert.Null(Text(tags[t], "kind")));
        Assert.All(parents, p => Assert.DoesNotContain(p, used));
        Assert.All(parents, p => Assert.False(string.IsNullOrWhiteSpace(Text(tags[p], "summary")), $"group {p} has no title"));
    }

    [Fact]
    public async Task FlatFileEndpoint_NestsUnderFiles_WithItsOwnDescription()
    {
        using var doc = await GetDocumentAsync();
        var tags = Tags(doc);

        Assert.All(OperationTags(doc).Where(o => o.Path.StartsWith("/api/{env}/files/CustomerData")), o => Assert.Equal("files/CustomerData", o.Tag));
        Assert.Equal("ns:Files", Text(tags["files/CustomerData"], "parent"));
        Assert.Equal("Files", Text(tags["ns:Files"], "summary"));
        Assert.Contains("Customer exports", Text(tags["files/CustomerData"], "description"));
        Assert.False(tags.ContainsKey("CustomerData"), "file TagDescription must not create a tag without operations");
    }

    [Fact]
    public async Task NamespaceGroup_includesNamespaceDescription()
    {
        using var doc = await GetDocumentAsync();
        var tags = Tags(doc);

        Assert.Contains("masterdata", Text(tags["ns:Masterdata"], "description"), StringComparison.OrdinalIgnoreCase);
    }

    // the samples are all namespaced, so the test writes its own flat endpoint
    [Fact]
    public async Task EndpointWithoutNamespace_NestsUnderTheDefaultGroup()
    {
        var dir = Path.Combine(Directory.GetCurrentDirectory(), "endpoints", "Proxy", "FlatTagProbe");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "entity.json"), """{ "Url": "http://localhost:8020/probe", "Methods": ["GET"], "AllowedEnvironments": ["500"] }""", TestContext.Current.CancellationToken);
        PortwayApi.Classes.EndpointHandler.ReloadAllEndpoints();
        try
        {
            using var doc = await GetDocumentAsync();
            var tags = Tags(doc);

            Assert.All(OperationTags(doc).Where(o => o.Path == "/api/{env}/FlatTagProbe"), o => Assert.Equal("FlatTagProbe", o.Tag));
            Assert.Equal("ns:General", Text(tags["FlatTagProbe"], "parent"));
            Assert.Equal("General", Text(tags["ns:General"], "summary"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
            PortwayApi.Classes.EndpointHandler.ReloadAllEndpoints();
        }
    }

    [Fact]
    public async Task Siblings_AreOrderedByTitle_EndpointsBeforeSubgroups()
    {
        using var doc = await GetDocumentAsync();
        var names = doc.RootElement.GetProperty("tags").EnumerateArray().Select(t => t.GetProperty("name").GetString()!).ToList();

        Assert.True(names.IndexOf("Production/Machines") < names.IndexOf("Production/Lines"), "Machine Details sorts before Production Line Data");
        Assert.True(names.IndexOf("CRM/Contacts") < names.IndexOf("ns:CRM/Documents"), "endpoints come before the nested group");
        Assert.Equal(names.IndexOf("ns:CRM/Documents") + 1, names.IndexOf("CRM/Documents/Attachments"));
        Assert.Contains("WMS/InboundBins", names);
    }

    [Fact]
    public async Task ShowNamespacesOff_ListsEveryEndpointFlat()
    {
        SetAllowedEnvironments("500", "700", "WMS");
        using var factory = _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration(c => c.AddInMemoryCollection(
            new Dictionary<string, string?> { ["OpenApi:ShowNamespaces"] = "false" })));
        var json = await factory.CreateClient().GetStringAsync("/docs/openapi.json", TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(json);
        var tags = Tags(doc);
        var used = OperationTags(doc).Select(o => o.Tag).ToHashSet(StringComparer.Ordinal);

        Assert.All(tags.Values, t => Assert.Null(Text(t, "parent")));
        Assert.All(tags.Values, t => Assert.Null(Text(t, "kind")));
        Assert.All(tags.Keys, name => Assert.Contains(name, used));
        Assert.Contains("CRM/Workflow/Requests", used);
        Assert.Contains("files/CustomerData", used);
        Assert.Contains("Masterdata/Classifications", used);
        Assert.Equal("Accounts", Text(tags["CRM/Accounts"], "summary"));
        Assert.Equal("Outstanding Items", Text(tags["Finance/OutstandingItems"], "summary"));
    }

    // the settings page saves these without a restart, so the document must follow a configuration reload
    [Fact]
    public async Task ShowNamespaces_AppliesOnConfigurationReload()
    {
        SetAllowedEnvironments("500", "700", "WMS");
        var toggle = new ToggleSource();
        using var factory = _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration(c => c.Add(toggle)));
        var client = factory.CreateClient();
        async Task<bool> HasGroups()
        {
            using var doc = JsonDocument.Parse(await client.GetStringAsync("/docs/openapi.json", TestContext.Current.CancellationToken));
            return Tags(doc).Values.Any(t => Text(t, "parent") is not null);
        }

        Assert.True(await HasGroups());

        toggle.Provider.Change("OpenApi:ShowNamespaces", "false");

        Assert.False(await HasGroups());
    }

    private sealed class ToggleSource : IConfigurationSource
    {
        public ToggleProvider Provider { get; } = new();
        public IConfigurationProvider Build(IConfigurationBuilder builder) => Provider;
    }

    private sealed class ToggleProvider : ConfigurationProvider
    {
        public void Change(string key, string value)
        {
            Data[key] = value;
            OnReload();
        }
    }

    // a nested namespace sits under its parent group and is titled by its own segment
    [Fact]
    public async Task NestedNamespace_NestsUnderItsParentGroup()
    {
        SetAllowedEnvironments("500", "700", "Synergy");
        using var doc = JsonDocument.Parse(await _client.GetStringAsync("/docs/openapi.json", TestContext.Current.CancellationToken));
        var tags = Tags(doc);

        Assert.Equal("ns:CRM/Workflow", Text(tags["CRM/Workflow/Requests"], "parent"));
        Assert.Equal("ns:CRM", Text(tags["ns:CRM/Workflow"], "parent"));
        Assert.Equal("Workflow", Text(tags["ns:CRM/Workflow"], "summary"));
    }
}
