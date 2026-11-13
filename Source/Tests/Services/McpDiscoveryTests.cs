using PortwayApi.Auth;
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

    private static AuthToken Caller(string environments) =>
        new() { Username = "caller", TokenHash = "h", TokenSalt = "s", AllowedEnvironments = environments };

    [Fact]
    public void CallerEnvironments_WithoutOwnEnvironments_ListsAllowedEnvironments() =>
        Assert.Equal(["WMS", "500"], PortwayMcpTools.CallerEnvironments(Versioned with { AllowedEnvironments = null }, ["WMS", "500"], Caller("*")));

    [Fact]
    public void CallerEnvironments_WithOwnEnvironments_KeepsThem() =>
        Assert.Equal(["700"], PortwayMcpTools.CallerEnvironments(Versioned with { AllowedEnvironments = ["700"] }, ["WMS", "500"], Caller("*")));

    [Fact]
    public void McpGetEndpointInfo_ReturnsCallerEnvironmentsOnly() =>
        Assert.Equal(["dev"], PortwayMcpTools.CallerEnvironments(Versioned with { AllowedEnvironments = ["dev", "prod"] }, ["WMS"], Caller("dev")));

    [Fact]
    public void McpListEndpoints_EnvironmentOutOfScope_Omitted()
    {
        Assert.Empty(PortwayMcpTools.CallerEnvironments(Versioned with { AllowedEnvironments = ["prod"] }, ["prod", "dev"], Caller("dev")));
        Assert.Empty(PortwayMcpTools.CallerEnvironments(Versioned with { AllowedEnvironments = null }, ["prod"], Caller("dev")));
    }

    [Fact]
    public void Describe_ListsGivenEnvironments() =>
        Assert.Equal(["dev"], PortwayMcpTools.Describe("crm_accounts", Versioned with { AllowedEnvironments = ["dev", "prod"] }, ["GET"], ["dev"]).AllowedEnvironments);
}
