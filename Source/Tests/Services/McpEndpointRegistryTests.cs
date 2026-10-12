using PortwayApi.Services.Mcp;
using Xunit;

namespace PortwayApi.Tests.Services;

/// <summary>
/// A registration swap drops tool definitions built from the previous one
/// </summary>
public class McpEndpointRegistryTests
{
    [Fact]
    public void DefinitionsCachedOnOldSnapshot_AreNotServedAfterRegister()
    {
        var registry = new McpEndpointRegistry();
        registry.RegisterEndpoints([new EndpointMcpInfo { Name = "Old", Methods = ["GET"] }]);
        var stale = registry.Current;

        registry.RegisterEndpoints([new EndpointMcpInfo { Name = "New", Methods = ["GET"] }]);
        stale.CachedToolDefinitions = new List<object>();

        Assert.Null(registry.Current.CachedToolDefinitions);
        Assert.Equal("New", Assert.Single(registry.Tools).EndpointName);
    }
}
