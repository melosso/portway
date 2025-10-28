using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.IO;

namespace PortwayApi.Tests.Base;

/// <summary>
/// Test fixture to provide shared setup and teardown for all tests
/// This ensures consistent test environment configuration
/// </summary>
public class PortwayApiTestFixture : IDisposable
{
    public IConfiguration Configuration { get; }
    public IServiceProvider ServiceProvider { get; }
    public ILogger<PortwayApiTestFixture> Logger { get; }
    
    private readonly string _tempDirectory;
    
    public PortwayApiTestFixture()
    {
        // Create a temporary directory for test artifacts
        _tempDirectory = Path.Combine(Path.GetTempPath(), "PortwayApiTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);
        
        // Setup configuration with test settings
        var configBuilder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.test.json", optional: false);
            
        Configuration = configBuilder.Build();
        
        // Set up DI container
        var services = new ServiceCollection();
        
        // Add configuration
        services.AddSingleton(Configuration);
        
        // Add basic logging
        services.AddLogging(builder => {
            builder.AddConfiguration(Configuration.GetSection("Logging"));
            builder.AddConsole();
        });
        
        // Build service provider
        ServiceProvider = services.BuildServiceProvider();
        
        Logger = ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger<PortwayApiTestFixture>();
            
        Logger.LogInformation("PortwayApi test fixture initialized. Temp directory: {TempDirectory}", _tempDirectory);
        
        // Create necessary directories for tests
        EnsureDirectoryStructure();
    }
    
    public void Dispose()
    {
        try
        {
            // Clean up temporary files
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error cleaning up test fixture resources");
        }
    }
    
    /// <summary>
    /// Creates necessary directory structure for tests to run properly
    /// This mimics the directory structure of the actual application
    /// </summary>
    private void EnsureDirectoryStructure()
    {
        string endpointsDir = Path.Combine(_tempDirectory, "endpoints");
        string sqlEndpointsDir = Path.Combine(endpointsDir, "SQL");
        string proxyEndpointsDir = Path.Combine(endpointsDir, "Proxy");
        string webhookDir = Path.Combine(endpointsDir, "Webhooks");
        string environmentsDir = Path.Combine(_tempDirectory, "environments");
        
        Directory.CreateDirectory(endpointsDir);
        Directory.CreateDirectory(sqlEndpointsDir);
        Directory.CreateDirectory(proxyEndpointsDir);
        Directory.CreateDirectory(webhookDir);
        Directory.CreateDirectory(environmentsDir);
        
        Logger.LogInformation("Created test directory structure in {TempDirectory}", _tempDirectory);
        
        // Create a sample SQL endpoint configuration for tests
        string sqlEntityContent = @"{
            ""DatabaseObjectName"": ""Items"",
            ""DatabaseSchema"": ""dbo"",
            ""PrimaryKey"": ""ItemCode"",
            ""AllowedColumns"": [
                ""ItemCode"",""Description"",""Assortment"",""sysguid""
            ]
        }";
        
        string productsDir = Path.Combine(sqlEndpointsDir, "Products");
        Directory.CreateDirectory(productsDir);
        File.WriteAllText(Path.Combine(productsDir, "entity.json"), sqlEntityContent);
        
        // Create a sample Proxy endpoint configuration for tests
        string proxyEntityContent = @"{ 
            ""Url"": ""http://localhost:8020/services/Exact.Entity.REST.EG/Account"", 
            ""Methods"": [""GET"", ""POST"", ""PUT"", ""DELETE"",""MERGE""] 
        }";
        
        string accountsDir = Path.Combine(proxyEndpointsDir, "Accounts");
        Directory.CreateDirectory(accountsDir);
        File.WriteAllText(Path.Combine(accountsDir, "entity.json"), proxyEntityContent);
        
        // Create environment settings
        string settingsContent = @"{
            ""Environment"": {
                ""ServerName"": ""localhost"",
                ""AllowedEnvironments"": [""600"", ""700""]
            }
        }";
        
        File.WriteAllText(Path.Combine(environmentsDir, "settings.json"), settingsContent);
        
        Logger.LogInformation("Created sample endpoint and environment configurations for tests");
    }
    
    /// <summary>
    /// Gets the path to the temporary test directory
    /// </summary>
    public string GetTempDirectory()
    {
        return _tempDirectory;
    }
}
