using PortwayApi.Services.Mcp;
using Xunit;

namespace PortwayApi.Tests.Services;

public class PortwayMcpToolsTests
{
    [Fact]
    public void Initialize_SetsStaticRegistry()
    {
        var registry = new McpEndpointRegistry();
        var appsProvider = new McpAppsResourceProvider();

        PortwayMcpTools.Initialize(registry, appsProvider);

        var endpoints = new[]
        {
            new EndpointMcpInfo
            {
                Name = "Products",
                Namespace = "inventory",
                Url = "/api/500/inventory/Products",
                Methods = new[] { "GET" }
            }
        };
        registry.RegisterEndpoints(endpoints);

        var result = PortwayMcpTools.ListEndpoints();
        Assert.Contains("Products", result);
    }

    [Fact]
    public void ListEndpoints_WhenNotInitialized_ReturnsErrorMessage()
    {
        var registry = new McpEndpointRegistry();
        var appsProvider = new McpAppsResourceProvider();
        PortwayMcpTools.Initialize(registry, appsProvider);

        var result = PortwayMcpTools.ListEndpoints();
        Assert.Equal("No endpoints registered", result);
    }

    [Fact]
    public void ListEndpoints_GroupsByNamespace()
    {
        var registry = new McpEndpointRegistry();
        var appsProvider = new McpAppsResourceProvider();
        PortwayMcpTools.Initialize(registry, appsProvider);

        var endpoints = new[]
        {
            new EndpointMcpInfo { Name = "Products", Namespace = "inventory", Url = "/api/500/inventory/Products", Methods = new[] { "GET" } },
            new EndpointMcpInfo { Name = "Warehouses", Namespace = "inventory", Url = "/api/500/inventory/Warehouses", Methods = new[] { "GET" } },
            new EndpointMcpInfo { Name = "Orders", Namespace = "sales", Url = "/api/500/sales/Orders", Methods = new[] { "GET" } }
        };
        registry.RegisterEndpoints(endpoints);

        var result = PortwayMcpTools.ListEndpoints();

        Assert.Contains("## inventory", result);
        Assert.Contains("## sales", result);
    }

    [Fact]
    public void GetEndpointInfo_WithNoEndpoints_ReturnsNotFound()
    {
        var registry = new McpEndpointRegistry();
        var appsProvider = new McpAppsResourceProvider();
        PortwayMcpTools.Initialize(registry, appsProvider);
        registry.RegisterEndpoints(Array.Empty<EndpointMcpInfo>());

        var result = PortwayMcpTools.GetEndpointInfo("Products");

        Assert.Equal("Endpoint 'Products' not found", result.Error);
    }

    [Fact]
    public void GetEndpointInfo_WhenNotFound_ReturnsError()
    {
        var registry = new McpEndpointRegistry();
        var appsProvider = new McpAppsResourceProvider();
        PortwayMcpTools.Initialize(registry, appsProvider);

        var endpoints = new[]
        {
            new EndpointMcpInfo { Name = "Products", Namespace = "inventory", Url = "/api/500/inventory/Products", Methods = new[] { "GET" } }
        };
        registry.RegisterEndpoints(endpoints);

        var result = PortwayMcpTools.GetEndpointInfo("NonExistent");

        Assert.Equal("Endpoint 'NonExistent' not found", result.Error);
    }

    [Fact]
    public void GetEndpointInfo_WhenFound_ReturnsDetails()
    {
        var registry = new McpEndpointRegistry();
        var appsProvider = new McpAppsResourceProvider();
        PortwayMcpTools.Initialize(registry, appsProvider);

        var endpoints = new[]
        {
            new EndpointMcpInfo
            {
                Name = "Products",
                Namespace = "inventory",
                Url = "/api/500/inventory/Products",
                Methods = new[] { "GET" },
                AllowedEnvironments = new[] { "500", "700" },
                UiEnabled = true
            }
        };
        registry.RegisterEndpoints(endpoints);

        var result = PortwayMcpTools.GetEndpointInfo("Products");

        Assert.Null(result.Error);
        Assert.Equal("Products", result.Name);
        Assert.Equal("inventory", result.Ns);
        Assert.Equal("GET", result.Method);
        Assert.Equal("/api/500/inventory/Products", result.Url);
        Assert.Equal(2, result.AllowedEnvironments.Count);
        Assert.True(result.HasUi);
        Assert.Equal("ui://endpoints/Products", result.UiUri);
    }

    [Fact]
    public void ListUiEnabledEndpoints_WhenNotInitialized_ReturnsEmpty()
    {
        var registry = new McpEndpointRegistry();
        var appsProvider = new McpAppsResourceProvider();
        PortwayMcpTools.Initialize(registry, appsProvider);

        var result = PortwayMcpTools.ListUiEnabledEndpoints();

        Assert.Equal(0, result.Count);
        Assert.Empty(result.Endpoints);
    }

    [Fact]
    public void ListUiEnabledEndpoints_WithUiEndpoints_ReturnsOnlyUiEndpoints()
    {
        var registry = new McpEndpointRegistry();
        var appsProvider = new McpAppsResourceProvider();
        PortwayMcpTools.Initialize(registry, appsProvider);

        var endpoints = new[]
        {
            new EndpointMcpInfo { Name = "Products", Url = "/api/500/Products", Methods = new[] { "GET" }, UiEnabled = true },
            new EndpointMcpInfo { Name = "Orders", Url = "/api/500/Orders", Methods = new[] { "GET" }, UiEnabled = false },
            new EndpointMcpInfo { Name = "Customers", Url = "/api/500/Customers", Methods = new[] { "GET" }, UiEnabled = true }
        };
        registry.RegisterEndpoints(endpoints);

        var result = PortwayMcpTools.ListUiEnabledEndpoints();

        Assert.Equal(2, result.Count);
        Assert.Contains(result.Endpoints, e => e.Name == "Products");
        Assert.Contains(result.Endpoints, e => e.Name == "Customers");
        Assert.DoesNotContain(result.Endpoints, e => e.Name == "Orders");
    }

    [Fact]
    public void ListUiEnabledEndpoints_DeduplicatesByEndpointName()
    {
        var registry = new McpEndpointRegistry();
        var appsProvider = new McpAppsResourceProvider();
        PortwayMcpTools.Initialize(registry, appsProvider);

        var endpoints = new[]
        {
            new EndpointMcpInfo { Name = "Products", Url = "/api/500/Products", Methods = new[] { "GET", "POST" }, UiEnabled = true }
        };
        registry.RegisterEndpoints(endpoints);

        var result = PortwayMcpTools.ListUiEnabledEndpoints();

        Assert.Equal(1, result.Count);
    }
}
