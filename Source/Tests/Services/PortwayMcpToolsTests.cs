using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PortwayApi.Auth;
using PortwayApi.Services.Mcp;
using Xunit;

namespace PortwayApi.Tests.Services;

/// <summary>
/// ListEndpoints/GetEndpointInfo/ListUiEnabledEndpoints must only show what the caller's own token can reach
/// </summary>
public class PortwayMcpToolsTests : IAsyncLifetime
{
    private string _dbPath = null!;
    private AuthDbContext _db = null!;
    private TokenService _tokenService = null!;
    private string _fullAccessToken = null!;
    private string _scopedToken = null!;

    public async ValueTask InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"portway_mcptools_{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<AuthDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        _db = new AuthDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        _tokenService = new TokenService(_db, new TokenVerificationCache(new MemoryCache(new MemoryCacheOptions())));
        _fullAccessToken = await _tokenService.GenerateTokenAsync($"full-{Guid.NewGuid():N}", allowedScopes: "*", allowedEnvironments: "*");
        _scopedToken = await _tokenService.GenerateTokenAsync($"scoped-{Guid.NewGuid():N}", allowedScopes: "inventory/Products", allowedEnvironments: "*");
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        return ValueTask.CompletedTask;
    }

    private static IHttpContextAccessor CallerWith(string token)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.Authorization = $"Bearer {token}";
        return new HttpContextAccessor { HttpContext = ctx };
    }

    private static IHttpContextAccessor NoCaller() => new HttpContextAccessor();

    [Fact]
    public async Task Initialize_SetsStaticRegistry()
    {
        var registry = new McpEndpointRegistry();
        PortwayMcpTools.Initialize(registry);

        var endpoints = new[]
        {
            new EndpointMcpInfo { Name = "Products", Namespace = "inventory", Url = "/api/500/inventory/Products", Methods = new[] { "GET" } }
        };
        registry.RegisterEndpoints(endpoints);

        var result = await PortwayMcpTools.ListEndpoints(CallerWith(_fullAccessToken), _tokenService);
        Assert.Contains("Products", result);
    }

    [Fact]
    public async Task ListEndpoints_WhenNotInitialized_ReturnsErrorMessage()
    {
        var registry = new McpEndpointRegistry();
        PortwayMcpTools.Initialize(registry);

        var result = await PortwayMcpTools.ListEndpoints(CallerWith(_fullAccessToken), _tokenService);
        Assert.Equal("No endpoints registered", result);
    }

    [Fact]
    public async Task ListEndpoints_GroupsByNamespace()
    {
        var registry = new McpEndpointRegistry();
        PortwayMcpTools.Initialize(registry);

        var endpoints = new[]
        {
            new EndpointMcpInfo { Name = "Products", Namespace = "inventory", Url = "/api/500/inventory/Products", Methods = new[] { "GET" } },
            new EndpointMcpInfo { Name = "Warehouses", Namespace = "inventory", Url = "/api/500/inventory/Warehouses", Methods = new[] { "GET" } },
            new EndpointMcpInfo { Name = "Orders", Namespace = "sales", Url = "/api/500/sales/Orders", Methods = new[] { "GET" } }
        };
        registry.RegisterEndpoints(endpoints);

        var result = await PortwayMcpTools.ListEndpoints(CallerWith(_fullAccessToken), _tokenService);

        Assert.Contains("## inventory", result);
        Assert.Contains("## sales", result);
    }

    [Fact]
    public async Task ListEndpoints_ShowsNameThatFindByNameAccepts()
    {
        var registry = new McpEndpointRegistry();
        PortwayMcpTools.Initialize(registry);

        var endpoints = new[]
        {
            new EndpointMcpInfo { Name = "Products", Namespace = "inventory", Url = "/api/500/inventory/Products", Methods = new[] { "GET" } }
        };
        registry.RegisterEndpoints(endpoints);

        var invokeName = Assert.Single(registry.ToolsByInvokeName.Keys);
        var listed = await PortwayMcpTools.ListEndpoints(CallerWith(_fullAccessToken), _tokenService);

        Assert.Contains(invokeName, listed, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(registry.FindByName(invokeName));
    }

    [Fact]
    public async Task ListEndpoints_HidesEndpointsOutsideCallerScope()
    {
        var registry = new McpEndpointRegistry();
        PortwayMcpTools.Initialize(registry);

        var endpoints = new[]
        {
            new EndpointMcpInfo { Name = "Products", Namespace = "inventory", Url = "/api/500/inventory/Products", Methods = new[] { "GET" } },
            new EndpointMcpInfo { Name = "Orders", Namespace = "sales", Url = "/api/500/sales/Orders", Methods = new[] { "GET" } }
        };
        registry.RegisterEndpoints(endpoints);

        var result = await PortwayMcpTools.ListEndpoints(CallerWith(_scopedToken), _tokenService);

        Assert.Contains("Products", result);
        Assert.DoesNotContain("Orders", result);
    }

    [Fact]
    public async Task ListEndpoints_WithNoCallerToken_ReturnsNothing()
    {
        var registry = new McpEndpointRegistry();
        PortwayMcpTools.Initialize(registry);

        var endpoints = new[]
        {
            new EndpointMcpInfo { Name = "Products", Namespace = "inventory", Url = "/api/500/inventory/Products", Methods = new[] { "GET" } }
        };
        registry.RegisterEndpoints(endpoints);

        var result = await PortwayMcpTools.ListEndpoints(NoCaller(), _tokenService);

        Assert.Equal("No endpoints registered", result);
    }

    [Fact]
    public async Task GetEndpointInfo_WithNoEndpoints_ReturnsNotFound()
    {
        var registry = new McpEndpointRegistry();
        PortwayMcpTools.Initialize(registry);
        registry.RegisterEndpoints(Array.Empty<EndpointMcpInfo>());

        var result = await PortwayMcpTools.GetEndpointInfo(CallerWith(_fullAccessToken), _tokenService, "Products");

        Assert.Equal("Endpoint 'Products' not found", result.Error);
    }

    [Fact]
    public async Task GetEndpointInfo_WhenNotFound_ReturnsError()
    {
        var registry = new McpEndpointRegistry();
        PortwayMcpTools.Initialize(registry);

        var endpoints = new[]
        {
            new EndpointMcpInfo { Name = "Products", Namespace = "inventory", Url = "/api/500/inventory/Products", Methods = new[] { "GET" } }
        };
        registry.RegisterEndpoints(endpoints);

        var result = await PortwayMcpTools.GetEndpointInfo(CallerWith(_fullAccessToken), _tokenService, "NonExistent");

        Assert.Equal("Endpoint 'NonExistent' not found", result.Error);
    }

    [Fact]
    public async Task GetEndpointInfo_WhenFound_ReturnsDetails()
    {
        var registry = new McpEndpointRegistry();
        PortwayMcpTools.Initialize(registry);

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

        var result = await PortwayMcpTools.GetEndpointInfo(CallerWith(_fullAccessToken), _tokenService, "inventory_Products");

        Assert.Null(result.Error);
        Assert.Equal("inventory_Products", result.InvokeName);
        Assert.Equal("Products", result.Name);
        Assert.Equal("inventory", result.Ns);
        Assert.Equal("GET", result.Method);
        Assert.Equal("/api/500/inventory/Products", result.Url);
        Assert.Equal(2, result.AllowedEnvironments.Count);
        Assert.True(result.HasUi);
        Assert.Equal("ui://endpoints/Products", result.UiUri);
    }

    [Fact]
    public async Task GetEndpointInfo_OutsideCallerScope_ReturnsNotFoundNotDetails()
    {
        var registry = new McpEndpointRegistry();
        PortwayMcpTools.Initialize(registry);

        var endpoints = new[]
        {
            new EndpointMcpInfo { Name = "Orders", Namespace = "sales", Url = "/api/500/sales/Orders", Methods = new[] { "GET" } }
        };
        registry.RegisterEndpoints(endpoints);

        var result = await PortwayMcpTools.GetEndpointInfo(CallerWith(_scopedToken), _tokenService, "sales_Orders");

        // Same "not found" a genuinely missing endpoint gets, so out-of-scope names cannot be probed for existence
        Assert.Equal("Endpoint 'sales_Orders' not found", result.Error);
        Assert.Null(result.Url);
    }

    [Fact]
    public async Task ListUiEnabledEndpoints_WhenNotInitialized_ReturnsEmpty()
    {
        var registry = new McpEndpointRegistry();
        PortwayMcpTools.Initialize(registry);

        var result = await PortwayMcpTools.ListUiEnabledEndpoints(CallerWith(_fullAccessToken), _tokenService);

        Assert.Equal(0, result.Count);
        Assert.Empty(result.Endpoints);
    }

    [Fact]
    public async Task ListUiEnabledEndpoints_WithUiEndpoints_ReturnsOnlyUiEndpoints()
    {
        var registry = new McpEndpointRegistry();
        PortwayMcpTools.Initialize(registry);

        var endpoints = new[]
        {
            new EndpointMcpInfo { Name = "Products", Url = "/api/500/Products", Methods = new[] { "GET" }, UiEnabled = true },
            new EndpointMcpInfo { Name = "Orders", Url = "/api/500/Orders", Methods = new[] { "GET" }, UiEnabled = false },
            new EndpointMcpInfo { Name = "Customers", Url = "/api/500/Customers", Methods = new[] { "GET" }, UiEnabled = true }
        };
        registry.RegisterEndpoints(endpoints);

        var result = await PortwayMcpTools.ListUiEnabledEndpoints(CallerWith(_fullAccessToken), _tokenService);

        Assert.Equal(2, result.Count);
        Assert.Contains(result.Endpoints, e => e.Name == "Products");
        Assert.Contains(result.Endpoints, e => e.Name == "Customers");
        Assert.DoesNotContain(result.Endpoints, e => e.Name == "Orders");
    }

    [Fact]
    public async Task ListUiEnabledEndpoints_DeduplicatesByEndpointName()
    {
        var registry = new McpEndpointRegistry();
        PortwayMcpTools.Initialize(registry);

        var endpoints = new[]
        {
            new EndpointMcpInfo { Name = "Products", Url = "/api/500/Products", Methods = new[] { "GET", "POST" }, UiEnabled = true }
        };
        registry.RegisterEndpoints(endpoints);

        var result = await PortwayMcpTools.ListUiEnabledEndpoints(CallerWith(_fullAccessToken), _tokenService);

        Assert.Equal(1, result.Count);
    }
}
