using PortwayApi.Classes;
using PortwayApi.Services.Mcp;
using Xunit;

namespace PortwayApi.Tests.Services;

public class McpRegistryStartupExtensionsTests
{
    private static EndpointDefinition ProxyEndpoint(
        bool enabled = true,
        McpSettings? mcp = null) => new()
    {
        FolderName = "Accounts",
        Namespace = "Account",
        Url = "http://localhost:8020/services/Account",
        Methods = new List<string> { "GET", "POST" },
        Type = EndpointType.Proxy,
        Enabled = enabled,
        AllowedEnvironments = new List<string> { "500", "700" },
        Mcp = mcp,
        Documentation = new Documentation
        {
            TagDescription = "Account Management",
            MethodDescriptions = new Dictionary<string, string>
            {
                ["GET"] = "Retrieve accounts",
                ["POST"] = "Create an account"
            }
        }
    };

    [Fact]
    public void BuildEndpointMcpInfos_ExposedProxyEndpoint_CarriesFullMetadata()
    {
        var endpoints = new Dictionary<string, EndpointDefinition>
        {
            ["Account/Accounts"] = ProxyEndpoint(mcp: new McpSettings { Exposed = true, Instruction = "Always filter by AccountCode." })
        };

        var result = McpRegistryStartupExtensions.BuildEndpointMcpInfos(endpoints).ToList();

        var info = Assert.Single(result);
        Assert.Equal("Accounts", info.Name);
        Assert.Equal("Account", info.Namespace);
        Assert.Equal("api", info.EndpointKind);
        Assert.Equal("Account Management", info.Description);
        Assert.Equal("Always filter by AccountCode.", info.Instruction);
        Assert.Equal(new[] { "500", "700" }, info.AllowedEnvironments);
        Assert.Equal("Retrieve accounts", info.MethodDescriptions?["GET"]);
        Assert.Equal(new[] { "GET", "POST" }, info.Methods);
    }

    [Fact]
    public void BuildEndpointMcpInfos_McpSettingsAbsent_DefaultsToExcluded()
    {
        var endpoints = new Dictionary<string, EndpointDefinition>
        {
            ["Account/Accounts"] = ProxyEndpoint(mcp: null)
        };

        var result = McpRegistryStartupExtensions.BuildEndpointMcpInfos(endpoints);

        Assert.Empty(result);
    }

    [Fact]
    public void BuildEndpointMcpInfos_ExposedFlagFalse_IsExcluded()
    {
        var endpoints = new Dictionary<string, EndpointDefinition>
        {
            ["Account/Accounts"] = ProxyEndpoint(mcp: new McpSettings { Exposed = false })
        };

        var result = McpRegistryStartupExtensions.BuildEndpointMcpInfos(endpoints);

        Assert.Empty(result);
    }

    [Fact]
    public void BuildEndpointMcpInfos_ExposedButDisabled_IsExcluded()
    {
        var endpoints = new Dictionary<string, EndpointDefinition>
        {
            ["Account/Accounts"] = ProxyEndpoint(enabled: false, mcp: new McpSettings { Exposed = true })
        };

        var result = McpRegistryStartupExtensions.BuildEndpointMcpInfos(endpoints);

        Assert.Empty(result);
    }

    [Fact]
    public void BuildEndpointMcpInfos_AliasedColumns_ExposesAliasNotRawColumn()
    {
        var endpoint = ProxyEndpoint(mcp: new McpSettings { Exposed = true });
        endpoint.AllowedColumns = new List<string> { "ItemCode;ProductNumber" };
        var endpoints = new Dictionary<string, EndpointDefinition> { ["Account/Accounts"] = endpoint };

        var result = McpRegistryStartupExtensions.BuildEndpointMcpInfos(endpoints).ToList();

        var fields = Assert.Single(result).AvailableFields;
        Assert.Equal(new[] { "ProductNumber" }, fields);
    }

    [Fact]
    public void BuildEndpointMcpInfos_FileEndpoint_ExposesGetOnly()
    {
        var endpoint = ProxyEndpoint(mcp: new McpSettings { Exposed = true });
        endpoint.Type = EndpointType.Files;
        endpoint.Methods = new List<string> { "GET", "POST", "DELETE" };
        var endpoints = new Dictionary<string, EndpointDefinition> { ["Documents"] = endpoint };

        var result = McpRegistryStartupExtensions.BuildEndpointMcpInfos(endpoints).ToList();

        var info = Assert.Single(result);
        Assert.Equal("file", info.EndpointKind);
        Assert.Equal(new[] { "GET" }, info.Methods);
    }
}
