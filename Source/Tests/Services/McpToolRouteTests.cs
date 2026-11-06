using PortwayApi.Classes;
using PortwayApi.Services.Mcp;
using Xunit;

namespace PortwayApi.Tests.Services;

/// <summary>
/// A path argument stays on the endpoint of the tool
/// </summary>
[Collection("Integration")]
public sealed class McpToolRouteTests : IDisposable
{
    private static readonly string Dir = Path.Combine("endpoints", "Proxy", "McpRoute");

    public McpToolRouteTests()
    {
        foreach (var relative in new[] { "Items", Path.Combine("Items", "Hidden") })
        {
            Directory.CreateDirectory(Path.Combine(Dir, relative));
            File.WriteAllText(Path.Combine(Dir, relative, "entity.json"),
                """{"Url":"http://localhost:9/x","Methods":["GET"],"AllowedEnvironments":["500"]}""");
        }
        EndpointHandler.ReloadEndpointType(EndpointType.Proxy);
    }

    public void Dispose()
    {
        Directory.Delete(Dir, recursive: true);
        EndpointHandler.ReloadEndpointType(EndpointType.Proxy);
    }

    private static readonly McpToolDescriptor Tool = new() { Namespace = "McpRoute", EndpointName = "Items", Method = "GET" };

    [Fact]
    public void Path_ResolvingToNeighbouringEndpoint_IsRefused()
    {
        Assert.True(EndpointHandler.GetProxyEndpoints().ContainsKey("McpRoute/Items/Hidden"));
        Assert.Null(McpChatService.BuildRoutePath(Tool, null, "Hidden"));
    }

    [Fact]
    public void Path_OnTheEndpoint_IsEscaped()
    {
        Assert.Equal("McpRoute/Items/a%20b", McpChatService.BuildRoutePath(Tool, null, "a b"));
    }
}
