using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using PortwayApi.Classes;
using PortwayApi.Helpers;
using PortwayApi.Interfaces;
using PortwayApi.Services;
using PortwayApi.Auth;
using PortwayApi;
using System.Net.Http.Headers;
using System.Text;

namespace PortwayApi.Tests.Base;

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

    public ApiTestBase()
    {
        _mockEnvironmentSettingsProvider = new Mock<IEnvironmentSettingsProvider>();
        _mockUrlValidator = new Mock<UrlValidator>(MockBehavior.Loose, "path");
        _mockODataToSqlConverter = new Mock<IODataToSqlConverter>();
        _mockConnectionPoolService = new Mock<SqlConnectionPoolService>(5, 100, 15, 30, true, "PortwayAPI");
        _mockSqlMetadataService = new Mock<SqlMetadataService>(_mockConnectionPoolService.Object);
        _mockTokenService = new Mock<TokenService>((AuthDbContext)null!);
        
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

        // Setup environment settings provider
        _mockEnvironmentSettingsProvider.Setup(p => p.LoadEnvironmentOrThrowAsync(It.IsAny<string>()))
            .ReturnsAsync(("Server=localhost;Database=test;Trusted_Connection=True", "localhost", new Dictionary<string, string>()));

        _mockEnvironmentSettingsProvider.Setup(p => p.GetEnvironmentConfigAsync(It.IsAny<string>()))
            .ReturnsAsync((string env) => new EnvironmentConfig 
            { 
                ConnectionString = "Server=localhost;Database=test;Trusted_Connection=True",
                ServerName = "localhost"
            });

        // Setup URL Validator
        _mockUrlValidator.Setup(v => v.IsUrlSafe(It.IsAny<string>())).Returns(true);

        // Configure the test server
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
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
