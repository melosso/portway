using PortwayApi.Services.Mcp;
using Xunit;

namespace PortwayApi.Tests.Services;

public class EndpointExplorerHtmlTests
{
    [Fact]
    public void Generate_WithEndpoints_ReturnsValidHtml()
    {
        var endpoints = new[]
        {
            new EndpointMcpInfo
            {
                Name = "Products",
                Namespace = "inventory",
                Url = "/api/500/inventory/Products",
                Methods = new[] { "GET", "POST" }
            }
        };

        var html = EndpointExplorerHtml.Generate(endpoints);

        Assert.Contains("<!DOCTYPE html>", html);
        Assert.Contains("Portway Endpoint Explorer", html);
        Assert.Contains("Products", html);
        Assert.Contains("inventory", html);
    }

    [Fact]
    public void Generate_WithMultipleEndpoints_IncludesAllEndpoints()
    {
        var endpoints = new[]
        {
            new EndpointMcpInfo { Name = "Products", Namespace = "inv", Url = "/api/500/inv/Products", Methods = new[] { "GET" } },
            new EndpointMcpInfo { Name = "Orders", Namespace = "sales", Url = "/api/500/sales/Orders", Methods = new[] { "GET", "POST" } }
        };

        var html = EndpointExplorerHtml.Generate(endpoints);

        Assert.Contains("Products", html);
        Assert.Contains("Orders", html);
    }

    [Fact]
    public void Generate_EscapesSpecialCharactersInNames()
    {
        var endpoints = new[]
        {
            new EndpointMcpInfo
            {
                Name = "Test<>/\"Endpoint",
                Namespace = "ns",
                Url = "/api/500/ns/Test",
                Methods = new[] { "GET" }
            }
        };

        var html = EndpointExplorerHtml.Generate(endpoints);

        Assert.DoesNotContain("<>", html);
        Assert.Contains("Test", html);
    }
}

public class McpEndpointRegistryTests
{
    [Fact]
    public void RegisterEndpoints_WithValidEndpoints_AddsToolsToRegistry()
    {
        var registry = new McpEndpointRegistry();

        var endpoints = new[]
        {
            new EndpointMcpInfo
            {
                Name = "Products",
                Namespace = "inventory",
                Url = "/api/500/inventory/Products",
                Methods = new[] { "GET" },
                AllowedEnvironments = new[] { "500" },
                Description = "Get products from inventory"
            }
        };

        registry.RegisterEndpoints(endpoints);

        var tools = registry.Tools;
        Assert.Single(tools);
        Assert.Equal("inventory_Products_GET", tools[0].Name);
    }

    [Fact]
    public void RegisterEndpoints_MultipleMethods_CreatesSeparateTools()
    {
        var registry = new McpEndpointRegistry();

        var endpoints = new[]
        {
            new EndpointMcpInfo
            {
                Name = "Products",
                Namespace = "inventory",
                Url = "/api/500/inventory/Products",
                Methods = new[] { "GET", "POST", "PUT" },
                Description = "Products endpoint"
            }
        };

        registry.RegisterEndpoints(endpoints);

        var tools = registry.Tools;
        Assert.Equal(3, tools.Count);
        Assert.Contains(tools, t => t.Name == "inventory_Products_GET");
        Assert.Contains(tools, t => t.Name == "inventory_Products_POST");
        Assert.Contains(tools, t => t.Name == "inventory_Products_PUT");
    }

    [Fact]
    public void RegisterEndpoints_NoNamespace_NameDoesNotHaveUnderscorePrefix()
    {
        var registry = new McpEndpointRegistry();

        var endpoints = new[]
        {
            new EndpointMcpInfo
            {
                Name = "Health",
                Url = "/api/500/Health",
                Methods = new[] { "GET" },
                Description = "Health check endpoint"
            }
        };

        registry.RegisterEndpoints(endpoints);

        var tools = registry.Tools;
        Assert.Single(tools);
        Assert.Equal("Health_GET", tools[0].Name);
    }

    [Fact]
    public void RegisterEndpoints_WithInstruction_AppendsToDescription()
    {
        var registry = new McpEndpointRegistry();

        var endpoints = new[]
        {
            new EndpointMcpInfo
            {
                Name = "Products",
                Url = "/api/500/Products",
                Methods = new[] { "GET" },
                Description = "Get products",
                Instruction = "Always filter by CategoryId"
            }
        };

        registry.RegisterEndpoints(endpoints);

        var tools = registry.Tools;
        Assert.Contains("Always filter by CategoryId", tools[0].Description);
    }

    [Fact]
    public void RegisterEndpoints_WithFields_IncludesInToolDescriptor()
    {
        var registry = new McpEndpointRegistry();

        var endpoints = new[]
        {
            new EndpointMcpInfo
            {
                Name = "Products",
                Url = "/api/500/Products",
                Methods = new[] { "GET" },
                AvailableFields = new[] { "Id", "Name", "Price" }
            }
        };

        registry.RegisterEndpoints(endpoints);

        var tools = registry.Tools;
        Assert.Equal(new[] { "Id", "Name", "Price" }, tools[0].AvailableFields);
    }

    [Fact]
    public void RegisterEndpoints_WithUiEnabled_SetsUiResourceUri()
    {
        var registry = new McpEndpointRegistry();

        var endpoints = new[]
        {
            new EndpointMcpInfo
            {
                Name = "Products",
                Url = "/api/500/Products",
                Methods = new[] { "GET" },
                UiEnabled = true
            }
        };

        registry.RegisterEndpoints(endpoints);

        var tools = registry.Tools;
        Assert.Equal("ui://endpoints/Products", tools[0].UiResourceUri);
    }

    [Fact]
    public void RegisterEndpoints_ClearsPreviousTools_BeforeAddingNew()
    {
        var registry = new McpEndpointRegistry();

        var firstSet = new[]
        {
            new EndpointMcpInfo
            {
                Name = "First",
                Url = "/api/500/First",
                Methods = new[] { "GET" }
            }
        };
        registry.RegisterEndpoints(firstSet);
        Assert.Single(registry.Tools);

        var secondSet = new[]
        {
            new EndpointMcpInfo
            {
                Name = "Second",
                Url = "/api/500/Second",
                Methods = new[] { "GET" }
            }
        };
        registry.RegisterEndpoints(secondSet);

        Assert.Single(registry.Tools);
        Assert.Equal("Second", registry.Tools[0].EndpointName);
    }

    [Fact]
    public void Clear_RemovesAllTools()
    {
        var registry = new McpEndpointRegistry();

        var endpoints = new[]
        {
            new EndpointMcpInfo
            {
                Name = "Products",
                Url = "/api/500/Products",
                Methods = new[] { "GET" }
            }
        };
        registry.RegisterEndpoints(endpoints);
        Assert.NotEmpty(registry.Tools);

        registry.Clear();

        Assert.Empty(registry.Tools);
    }

    [Fact]
    public void Refresh_InvokesRefreshAction()
    {
        var registry = new McpEndpointRegistry();
        var refreshCalled = false;
        registry.RefreshAction = () => refreshCalled = true;

        registry.Refresh();

        Assert.True(refreshCalled);
    }
}
