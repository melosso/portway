using PortwayApi.Services.Mcp;
using Xunit;

namespace PortwayApi.Tests.Services;

/// <summary>
/// MCP discovery lifecycle and environments
/// </summary>
public class McpDiscoveryTests
{
    private static readonly McpToolDescriptor Versioned = new()
    {
        Name = "warehouse_warehouses_v2_GET",
        EndpointName = "Warehouses",
        Namespace = "Warehouse",
        Version = "v2",
        Deprecated = true,
        Sunset = new DateTimeOffset(2027, 3, 1, 0, 0, 0, TimeSpan.Zero),
        Expandable = ["CountryInfo"],
        Method = "GET"
    };

    [Fact]
    public void LifecycleTags_NameVersionDeprecationSunsetAndExpand() =>
        Assert.Equal(" [version: v2] [deprecated] [sunset: 2027-03-01] [expand: CountryInfo]", Versioned.LifecycleTags);

    [Fact]
    public void LifecycleTags_EmptyForDefaultVersion() =>
        Assert.Equal("", new McpToolDescriptor { EndpointName = "Items", Method = "GET" }.LifecycleTags);

    [Fact]
    public void Describe_IncludesLifecycleAndExpandable()
    {
        var info = PortwayMcpTools.Describe("warehouse_warehouses_v2", Versioned, ["GET"], ["WMS"]);

        Assert.Equal("v2", info.Version);
        Assert.True(info.Deprecated);
        Assert.Equal(Versioned.Sunset, info.Sunset);
        Assert.Equal(["CountryInfo"], info.Expandable);
    }

    [Fact]
    public void Describe_WithoutOwnEnvironments_ListsAllowedEnvironments() =>
        Assert.Equal(["WMS", "500"], PortwayMcpTools.Describe("crm_accounts", Versioned with { AllowedEnvironments = null }, ["GET"], ["WMS", "500"]).AllowedEnvironments);

    [Fact]
    public void Describe_WithOwnEnvironments_KeepsThem() =>
        Assert.Equal(["700"], PortwayMcpTools.Describe("crm_accounts", Versioned with { AllowedEnvironments = ["700"] }, ["GET"], ["WMS", "500"]).AllowedEnvironments);
}
