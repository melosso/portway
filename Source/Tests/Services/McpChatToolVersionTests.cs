using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PortwayApi.Services.Mcp;
using PortwayApi.Tests.Base;
using Xunit;

namespace PortwayApi.Tests.Services;

/// <summary>
/// Each endpoint version is its own chat tool and resolves to that version
/// </summary>
public class McpChatToolVersionTests
{
    private static (McpChatService Service, McpEndpointRegistry Registry) Build(params EndpointMcpInfo[] endpoints)
    {
        var registry = new McpEndpointRegistry();
        registry.RegisterEndpoints(endpoints);
        var service = new McpChatService(registry, Options.Create(new McpOptions()), null!, null!,
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), new TestEnvironmentSettings());
        return (service, registry);
    }

    private static EndpointMcpInfo Items(string? version, bool deprecated = false, params string[] methods) => new()
    {
        Name = "Items",
        Namespace = "Inventory",
        Version = version,
        Deprecated = deprecated,
        Methods = methods.Length > 0 ? methods : ["GET"]
    };

    [Fact]
    public void Versions_AreSeparateTools()
    {
        var (service, _) = Build(Items(null), Items("v2", methods: ["GET", "POST"]));

        var tools = service.GetToolDefinitions();

        Assert.Equal(["inventory_items", "inventory_items_v2"], tools.Select(t => t.Name).Order());
        Assert.Contains("(methods: GET)", tools.Single(t => t.Name == "inventory_items").Description);
        Assert.Contains("[version: v2]", tools.Single(t => t.Name == "inventory_items_v2").Description);
    }

    [Fact]
    public void EveryAdvertisedTool_ResolvesToItsVersion()
    {
        var (service, registry) = Build(Items("v2"));

        var tool = Assert.Single(service.GetToolDefinitions());

        Assert.Equal("v2", registry.FindByName(tool.Name)?.Version);
    }

    [Fact]
    public void DeprecatedVersion_IsMarked()
    {
        var (service, _) = Build(Items(null, deprecated: true), Items("v2"));

        var tools = service.GetToolDefinitions();

        Assert.Contains("[deprecated]", tools.Single(t => t.Name == "inventory_items").Description);
        Assert.DoesNotContain("[deprecated]", tools.Single(t => t.Name == "inventory_items_v2").Description);
    }

    [Fact]
    public void Expandable_IsListed()
    {
        var (service, _) = Build(Items(null) with { Expandable = ["Category", "Supplier"] });

        Assert.Contains("[expand: Category, Supplier]", Assert.Single(service.GetToolDefinitions()).Description);
    }
}
