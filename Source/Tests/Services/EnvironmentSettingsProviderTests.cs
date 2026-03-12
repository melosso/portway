using PortwayApi.Classes;
using PortwayApi.Helpers;
using System.Text.Json;
using Xunit;
using System.Reflection;

namespace PortwayApi.Tests.Services;

public class EnvironmentSettingsProviderTests : IDisposable
{
    private readonly string _testBaseDir;
    private readonly string _environmentsDir;
    private readonly string _envName;

    public EnvironmentSettingsProviderTests()
    {
        _envName = "test-encrypt-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        _testBaseDir = Path.Combine(Directory.GetCurrentDirectory(), "TestEnvironments_" + Guid.NewGuid().ToString("N"));
        _environmentsDir = Path.Combine(_testBaseDir, "environments");
        Directory.CreateDirectory(_environmentsDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testBaseDir))
            {
                Directory.Delete(_testBaseDir, true);
            }
        }
        catch { }
    }

    private EnvironmentSettingsProvider CreateProvider()
    {
        var provider = new EnvironmentSettingsProvider(); 
        var fieldBasePath = typeof(EnvironmentSettingsProvider).GetField("_basePath", BindingFlags.NonPublic | BindingFlags.Instance);
        fieldBasePath!.SetValue(provider, _environmentsDir);
        return provider;
    }

    [Fact]
    public void EncryptEnvironmentIfNeeded_EncryptsConnectionStringAndHeaders()
    {
        var provider = CreateProvider();
        
        var envDir = Path.Combine(_environmentsDir, _envName);
        Directory.CreateDirectory(envDir);
        var settingsPath = Path.Combine(envDir, "settings.json");
        
        var config = new EnvironmentConfig
        {
            ConnectionString = "Server=myServerAddress;Database=myDataBase;User Id=myUsername;Password=myPassword;",
            ServerName = "TestServer",
            Headers = new Dictionary<string, string>
            {
                { "Authorization", "Bearer my-secret-token" },
                { "Custom-Header", "public-value" }
            },
            Authentication = new AuthenticationSettings
            {
                Enabled = true,
                Methods = new List<AuthenticationMethod>
                {
                    new AuthenticationMethod { Type = "Hmac", Name = "X-Sig", Secret = "hmac-secret-123", Value = "sensitive-val", ClientSecret = "client-secret-abc" }
                }
            }
        };
        
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(settingsPath, JsonSerializer.Serialize(config, jsonOptions));

        provider.EncryptEnvironmentIfNeeded(_envName);

        var resultJson = File.ReadAllText(settingsPath);
        var resultConfig = JsonSerializer.Deserialize<EnvironmentConfig>(resultJson)!;

        Assert.True(SettingsEncryptionHelper.IsEncrypted(resultConfig.ConnectionString!));
        Assert.True(SettingsEncryptionHelper.IsEncrypted(resultConfig.Headers!["Authorization"]));
        Assert.False(SettingsEncryptionHelper.IsEncrypted(resultConfig.Headers["Custom-Header"]));
        
        var method = resultConfig.Authentication!.Methods![0];
        Assert.True(SettingsEncryptionHelper.IsEncrypted(method.Secret!));
        Assert.True(SettingsEncryptionHelper.IsEncrypted(method.Value!));
        Assert.True(SettingsEncryptionHelper.IsEncrypted(method.ClientSecret!));
    }

    [Fact]
    public async Task LoadEnvironmentOrThrowAsync_DecryptsSuccessfully()
    {
        var provider = CreateProvider();
        
        var envDir = Path.Combine(_environmentsDir, _envName);
        Directory.CreateDirectory(envDir);
        var settingsPath = Path.Combine(envDir, "settings.json");
        
        var config = new EnvironmentConfig
        {
            ConnectionString = "Server=myServer;Database=myDb;User Id=user;Password=pass;",
            ServerName = "TestServer",
            Headers = new Dictionary<string, string>
            {
                { "Authorization", "secret-token" }
            },
            Authentication = new AuthenticationSettings
            {
                Enabled = true,
                Methods = new List<AuthenticationMethod>
                {
                    new AuthenticationMethod { Type = "Hmac", Name = "X-Sig", Secret = "hmac-secret-123" }
                }
            }
        };
        
        File.WriteAllText(settingsPath, JsonSerializer.Serialize(config));
        
        provider.EncryptEnvironmentIfNeeded(_envName);
        
        var result = await provider.LoadEnvironmentOrThrowAsync(_envName);
        
        Assert.NotNull(result.ConnectionString);
        Assert.Contains("myServer", result.ConnectionString);
        Assert.Equal("TestServer", result.ServerName);
        
        var fullConfig = await provider.GetEnvironmentConfigAsync(_envName);
        Assert.Equal("secret-token", fullConfig!.Headers!["Authorization"]);
        Assert.Equal("hmac-secret-123", fullConfig.Authentication!.Methods![0].Secret);
    }

    [Fact]
    public void EncryptEnvironmentIfNeeded_SkipsIfInvalidConnectionString()
    {
        var provider = CreateProvider();
        
        var envDir = Path.Combine(_environmentsDir, _envName);
        Directory.CreateDirectory(envDir);
        var settingsPath = Path.Combine(envDir, "settings.json");
        
        var config = new EnvironmentConfig
        {
            ConnectionString = "Invalid Connection String format",
        };
        
        File.WriteAllText(settingsPath, JsonSerializer.Serialize(config));

        provider.EncryptEnvironmentIfNeeded(_envName);

        var resultJson = File.ReadAllText(settingsPath);
        var resultConfig = JsonSerializer.Deserialize<EnvironmentConfig>(resultJson)!;

        Assert.Equal("Invalid Connection String format", resultConfig.ConnectionString);
        Assert.False(SettingsEncryptionHelper.IsEncrypted(resultConfig.ConnectionString!));
    }
}
