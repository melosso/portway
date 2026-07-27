using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Moq;
using PortwayApi.Classes;
using PortwayApi.Services.Providers;
using PortwayApi.Helpers;
using PortwayApi.Interfaces;
using PortwayApi.Services;
using PortwayApi.Auth;
using PortwayApi;
using System.Net.Http.Headers;
using System.Text;

namespace PortwayApi.Tests.Base;

/// <summary>xUnit collection that serializes all integration tests sharing WebApplicationFactory. Without this, parallel factory creation races on SQLite file access and the MCP HTTP transport</summary>
[CollectionDefinition("Integration")]
public class IntegrationTestCollection { }

[Collection("Integration")]
public class ApiTestBase : IDisposable
{
    protected readonly HttpClient _client;
    protected readonly Mock<IEnvironmentSettingsProvider> _mockEnvironmentSettingsProvider;
    protected readonly Mock<UrlValidator> _mockUrlValidator;
    protected readonly Mock<IODataToSqlConverter> _mockODataToSqlConverter;
    protected readonly Mock<SqlConnectionPoolService> _mockConnectionPoolService;
    protected readonly Mock<SqlMetadataService> _mockSqlMetadataService;
    protected readonly Mock<TokenService> _mockTokenService;
    protected readonly WebApplicationFactory<Program> _factory;

    // Instead of mocking EnvironmentSettings, we'll create a test implementation
    protected readonly TestEnvironmentSettings _testEnvironmentSettings;

    // Unique per-instance SQLite paths so parallel test runs don't race on the same file
    private readonly string _authDbPath;
    private readonly string _mcpDbPath;

    public ApiTestBase()
    {
        var id = Guid.NewGuid().ToString("N");
        _authDbPath = Path.Combine(Path.GetTempPath(), $"portway_test_{id}_auth.db");
        _mcpDbPath  = Path.Combine(Path.GetTempPath(), $"portway_test_{id}_mcp.db");

        _mockEnvironmentSettingsProvider = new Mock<IEnvironmentSettingsProvider>();
        _mockUrlValidator = new Mock<UrlValidator>(MockBehavior.Loose, "path");
        _mockODataToSqlConverter = new Mock<IODataToSqlConverter>();

        // 1s connect timeout; no SQL Server runs under test so the default 15s wait would dominate the suite
        var poolingOptions = new SqlPoolingOptions(5, 100, 1, true, "PortwayAPI");

        // Real factory (this is not a mock) so the WMS SQLite demo.db resolves to SqliteProvider
        var providerFactory = new SqlProviderFactory(
            [new MsSqlProvider(), new PostgreSqlProvider(), new MySqlProvider(), new SqliteProvider()]);

        _mockConnectionPoolService = new Mock<SqlConnectionPoolService>(poolingOptions, providerFactory);
        _mockSqlMetadataService = new Mock<SqlMetadataService>(_mockConnectionPoolService.Object, providerFactory);
        _mockTokenService = new Mock<TokenService>((AuthDbContext)null!, (ITokenVerificationCache)null!);
        
        // Setup token service mock
        _mockTokenService.Setup(s => s.VerifyTokenAsync("test-token"))
            .ReturnsAsync(true);
        
        _mockTokenService.Setup(s => s.GetActiveTokensAsync())
            .ReturnsAsync(new List<AuthToken>());
        
        _mockTokenService.Setup(s => s.GetTokenDetailsByTokenAsync("test-token"))
            .ReturnsAsync(new AuthToken 
            { 
                Username = "test-user", 
                TokenHash = "hash", 
                TokenSalt = "salt",
                AllowedEnvironments = "*", 
                AllowedScopes = "*"
            });

        // Create a test implementation that we can control directly
        _testEnvironmentSettings = new TestEnvironmentSettings();
        _testEnvironmentSettings.SetAllowedEnvironments(new List<string> { "500", "700" });

        // WMS maps to the SQLite demo database; other environments keep the unreachable SQL Server string
        _mockEnvironmentSettingsProvider.Setup(p => p.LoadEnvironmentOrThrowAsync(It.IsAny<string>()))
            .ReturnsAsync((string env) => (ConnectionStringFor(env), "localhost", new Dictionary<string, string>()));

        _mockEnvironmentSettingsProvider.Setup(p => p.GetEnvironmentConfigAsync(It.IsAny<string>()))
            .ReturnsAsync((string env) => new EnvironmentConfig
            {
                ConnectionString = ConnectionStringFor(env),
                ServerName = "localhost"
            });

        // Setup URL Validator
        _mockUrlValidator.Setup(v => v.IsUrlSafe(It.IsAny<string>())).Returns(true);

        // Configure the test server
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration(config =>
                {
                    // Use per-instance SQLite paths and disable the MCP HTTP server in tests; The MCP HTTP transport registers a hosted service that conflicts when multiple; WebApplicationFactory instances start in parallel
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Mcp:Enabled"] = "false"
                    });
                });

                builder.ConfigureTestServices(services =>
                {
                    // Isolate SQLite databases per test instance to prevent file-lock races
                    services.AddDbContext<PortwayApi.Auth.AuthDbContext>(opts =>
                        opts.UseSqlite($"Data Source={_authDbPath}"),
                        ServiceLifetime.Scoped, ServiceLifetime.Scoped);
                    services.AddDbContextFactory<PortwayApi.Services.Mcp.McpConfigDbContext>(opts =>
                        opts.UseSqlite($"Data Source={_mcpDbPath}"));

                    // Replace services with mocks
                    services.AddSingleton(_mockEnvironmentSettingsProvider.Object);
                    services.AddSingleton<EnvironmentSettings>(_testEnvironmentSettings); // Use our test implementation
                    services.AddSingleton(_mockUrlValidator.Object);
                    services.AddSingleton(_mockODataToSqlConverter.Object);
                    services.AddSingleton(_mockConnectionPoolService.Object);
                    services.AddSingleton(_mockSqlMetadataService.Object);
                    services.AddSingleton(_mockTokenService.Object);
                    
                    // Disable rate limiting for tests
                    services.Configure<PortwayApi.Middleware.RateLimitSettings>(options =>
                    {
                        options.Enabled = false;
                    });
                    
                    // Configure minimal logging for tests
                    services.AddLogging(logging =>
                    {
                        logging.ClearProviders();
                        logging.AddConsole();
                        logging.SetMinimumLevel(LogLevel.Warning);
                    });
                });
            });

        _client = _factory.CreateClient();
        
        // Add default authorization header with test token
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        if (File.Exists(_authDbPath)) File.Delete(_authDbPath);
        if (File.Exists(_mcpDbPath))  File.Delete(_mcpDbPath);
    }

    // Helper method to add authorization header
    protected void AddAuthorizationHeader(string token = "test-token")
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
    
    // Helper method to set allowed environments for a test
    protected void SetAllowedEnvironments(params string[] environments)
    {
        _testEnvironmentSettings.SetAllowedEnvironments(environments.ToList());
    }

    /// <summary>Absolute path to the WMS SQLite demo database copied next to the test assembly</summary>
    protected static string WmsDemoDbPath =>
        Path.Combine(AppContext.BaseDirectory, "environments", "WMS", "demo.db");

    /// <summary>True when the WMS demo database is present, so shape tests can assert instead of skipping</summary>
    protected static bool WmsDemoDbAvailable => File.Exists(WmsDemoDbPath);

    private static string ConnectionStringFor(string environment) =>
        string.Equals(environment, "WMS", StringComparison.OrdinalIgnoreCase)
            ? $"Data Source={WmsDemoDbPath}"
            : "Server=localhost;Database=test;Trusted_Connection=True";
}

// Test implementation that we can control directly without mocking
public class TestEnvironmentSettings : EnvironmentSettings
{
    private List<string> _allowedEnvironments = new List<string> { "500", "700" };
    
    public void SetAllowedEnvironments(List<string> environments)
    {
        _allowedEnvironments = environments.ToList();
    }
    
    public override bool IsEnvironmentAllowed(string environment)
    {
        return _allowedEnvironments.Contains(environment, StringComparer.OrdinalIgnoreCase);
    }

    public override List<string> GetAllowedEnvironments()
    {
        return _allowedEnvironments.ToList();
    }

}
