using System.Reflection;
using System.Text.Json;
using Azure;
using Azure.Security.KeyVault.Secrets;
using Moq;
using PortwayApi.Classes;
using PortwayApi.Helpers;
using PortwayApi.Tests.Support;
using Xunit;

namespace PortwayApi.Tests.Services;

public sealed class SecretStoreTests : IDisposable
{
    private const string Env = "SECRETS";
    private readonly TempDirectory _root = new("SecretStore", Directory.GetCurrentDirectory());
    private readonly FakeTimeProvider _time = new();
    private readonly Mock<SecretClient> _vault = new();

    public SecretStoreTests()
    {
        Directory.CreateDirectory(_root.Combine("environments", Env));
        _vault.Setup(c => c.GetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "not found"));
    }

    public void Dispose() => _root.Dispose();

    private string SettingsPath => _root.Combine("environments", Env, "settings.json");

    private EnvironmentSettingsProvider Provider(bool withVault = false)
    {
        var provider = new EnvironmentSettingsProvider(withVault ? _vault.Object : null, _time);
        typeof(EnvironmentSettingsProvider).GetField("_basePath", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(provider, _root.Combine("environments"));
        return provider;
    }

    private void WriteSettings(object settings) => File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings));

    private void VaultReturns(string name, string value) =>
        _vault.Setup(c => c.GetSecretAsync(name, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(SecretModelFactory.KeyVaultSecret(new SecretProperties(name), value), Mock.Of<Response>()));

    private void VaultThrows(string name, Exception ex) =>
        _vault.Setup(c => c.GetSecretAsync(name, It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(ex);

    [Fact]
    public void Secrets_EncryptedAtStartup()
    {
        WriteSettings(new Dictionary<string, object> { ["ConnectionString"] = "Server=s;Database=d", ["Secrets"] = new Dictionary<string, string> { ["ERP-KEY"] = "plain-key" } });

        Provider().EncryptEnvironmentIfNeeded(Env);

        var saved = File.ReadAllText(SettingsPath);
        Assert.DoesNotContain("plain-key", saved);
        Assert.True(SettingsEncryptionHelper.IsEncrypted(JsonSerializer.Deserialize<EnvironmentConfig>(saved)!.Secrets!["ERP-KEY"]));
    }

    [Fact]
    public void Secrets_EncryptFalse_StaysPlaintext()
    {
        WriteSettings(new Dictionary<string, object> { ["Encrypt"] = false, ["Secrets"] = new Dictionary<string, string> { ["ERP-KEY"] = "plain-key" } });

        Provider().EncryptEnvironmentIfNeeded(Env);

        Assert.Contains("plain-key", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public async Task Secrets_DecryptedOnLoad()
    {
        WriteSettings(new Dictionary<string, object> { ["Secrets"] = new Dictionary<string, string> { ["ERP-KEY"] = "plain-key" } });
        var provider = Provider();
        provider.EncryptEnvironmentIfNeeded(Env);

        Assert.Equal("plain-key", await provider.GetSecretAsync(Env, "erp-key", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void EncryptionSave_KeepsUnknownKeys()
    {
        File.WriteAllText(SettingsPath, """{"ConnectionString":"Server=s;Database=d;Password=p","FutureKey":{"Nested":[1,2]}}""");

        Provider().EncryptEnvironmentIfNeeded(Env);

        using var saved = JsonDocument.Parse(File.ReadAllText(SettingsPath));
        Assert.Equal(2, saved.RootElement.GetProperty("FutureKey").GetProperty("Nested").GetArrayLength());
    }

    [Theory]
    [InlineData("bad name")]
    [InlineData("under_score")]
    public async Task Secrets_InvalidName_Refused(string name)
    {
        WriteSettings(new Dictionary<string, object> { ["Encrypt"] = false, ["Secrets"] = new Dictionary<string, string> { [name] = "x", ["GOOD"] = "y" } });
        var provider = Provider();

        Assert.Null(await provider.GetSecretAsync(Env, name, TestContext.Current.CancellationToken));
        Assert.Equal("y", await provider.GetSecretAsync(Env, "GOOD", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Secrets_CaseDuplicate_Refused()
    {
        File.WriteAllText(SettingsPath, """{"Encrypt":false,"Secrets":{"ERP-KEY":"a","erp-key":"b"}}""");

        Assert.Null(await Provider().GetSecretAsync(Env, "ERP-KEY", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Secrets_EmptyValue_Refused(string value)
    {
        WriteSettings(new Dictionary<string, object> { ["Encrypt"] = false, ["Secrets"] = new Dictionary<string, string> { ["EMPTY"] = value } });

        Assert.Null(await Provider().GetSecretAsync(Env, "EMPTY", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConnectionString_ResolvesReference()
    {
        WriteSettings(new Dictionary<string, object>
        {
            ["Encrypt"] = false,
            ["ConnectionString"] = "Server=s;Database=d;Password=${secret:DB-PASSWORD}",
            ["Secrets"] = new Dictionary<string, string> { ["DB-PASSWORD"] = "a;Database=evil" }
        });

        var (connectionString, _, _) = await Provider().LoadEnvironmentOrThrowAsync(Env);

        var builder = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = connectionString };
        Assert.Equal("d", builder["Database"]);
        Assert.Equal("a;Database=evil", builder["Password"]);
    }

    [Fact]
    public async Task ConnectionString_MissingSecret_Throws()
    {
        WriteSettings(new Dictionary<string, object> { ["Encrypt"] = false, ["ConnectionString"] = "Server=s;Password=${secret:NOPE}" });

        await Assert.ThrowsAsync<SecretResolutionException>(() => Provider().LoadEnvironmentOrThrowAsync(Env));
    }

    [Fact]
    public void ConnectionString_SemicolonSecret_AbsentFromDebugLog()
    {
        var masked = EnvironmentSettingsProvider.MaskConnectionString("Server=s;Password=\"a;Secret=leak\";User ID=svc");

        Assert.DoesNotContain("leak", masked);
        Assert.DoesNotContain("svc", masked);
        Assert.Contains("server=s", masked, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MalformedSettings_ServesLastGood()
    {
        WriteSettings(new Dictionary<string, object> { ["Encrypt"] = false, ["ServerName"] = "good" });
        var provider = Provider();
        Assert.Equal("good", (await provider.GetEnvironmentConfigAsync(Env))!.ServerName);

        File.WriteAllText(SettingsPath, "{ not json");
        File.SetLastWriteTimeUtc(SettingsPath, DateTime.UtcNow.AddSeconds(5));

        Assert.Equal("good", (await provider.GetEnvironmentConfigAsync(Env))!.ServerName);
    }

    [Fact]
    public async Task MalformedSettings_NoLastGood_Throws()
    {
        File.WriteAllText(SettingsPath, "{ not json");

        await Assert.ThrowsAnyAsync<Exception>(() => Provider().GetEnvironmentConfigAsync(Env));
    }

    [Fact]
    public async Task AbsentSettings_ReturnsNull()
    {
        File.Delete(SettingsPath);

        Assert.Null(await Provider().GetEnvironmentConfigAsync(Env));
    }

    [Fact]
    public async Task KeyVault_ValueWinsOverFile()
    {
        WriteSettings(new Dictionary<string, object> { ["Encrypt"] = false, ["Secrets"] = new Dictionary<string, string> { ["ERP-KEY"] = "file" } });
        VaultReturns($"{Env}-Secret-ERP-KEY", "vault");

        Assert.Equal("vault", await Provider(withVault: true).GetSecretAsync(Env, "ERP-KEY", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task KeyVault_NotFound_UsesFile()
    {
        WriteSettings(new Dictionary<string, object> { ["Encrypt"] = false, ["Secrets"] = new Dictionary<string, string> { ["ERP-KEY"] = "file" } });

        Assert.Equal("file", await Provider(withVault: true).GetSecretAsync(Env, "ERP-KEY", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task KeyVaultFailure_UsesLastKnownValue()
    {
        var provider = Provider(withVault: true);
        VaultReturns($"{Env}-Secret-ERP-KEY", "vault");
        Assert.Equal("vault", await provider.GetSecretAsync(Env, "ERP-KEY", TestContext.Current.CancellationToken));

        VaultThrows($"{Env}-Secret-ERP-KEY", new RequestFailedException(503, "down"));
        _time.Advance(TimeSpan.FromMinutes(30));

        Assert.Equal("vault", await provider.GetSecretAsync(Env, "ERP-KEY", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task KeyVaultLastKnown_ExpiresAtMaximumAge()
    {
        var provider = Provider(withVault: true);
        VaultReturns($"{Env}-Secret-ERP-KEY", "vault");
        await provider.GetSecretAsync(Env, "ERP-KEY", TestContext.Current.CancellationToken);

        VaultThrows($"{Env}-Secret-ERP-KEY", new RequestFailedException(503, "down"));
        _time.Advance(TimeSpan.FromMinutes(58));
        Assert.Equal("vault", await provider.GetSecretAsync(Env, "ERP-KEY", TestContext.Current.CancellationToken));
        _time.Advance(TimeSpan.FromMinutes(3));

        Assert.Null(await provider.GetSecretAsync(Env, "ERP-KEY", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task KeyVaultDisabled_StopsUsingValue()
    {
        WriteSettings(new Dictionary<string, object> { ["Encrypt"] = false, ["Secrets"] = new Dictionary<string, string> { ["ERP-KEY"] = "file" } });
        var provider = Provider(withVault: true);
        VaultReturns($"{Env}-Secret-ERP-KEY", "vault");
        await provider.GetSecretAsync(Env, "ERP-KEY", TestContext.Current.CancellationToken);

        VaultThrows($"{Env}-Secret-ERP-KEY", new RequestFailedException(403, "disabled", "SecretDisabled", null));
        _time.Advance(EnvironmentSettingsProvider.KeyVaultValueLifetime);
        Assert.Null(await provider.GetSecretAsync(Env, "ERP-KEY", TestContext.Current.CancellationToken));

        VaultThrows($"{Env}-Secret-ERP-KEY", new RequestFailedException(503, "down"));
        _time.Advance(EnvironmentSettingsProvider.KeyVaultMissLifetime);
        Assert.Null(await provider.GetSecretAsync(Env, "ERP-KEY", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task KeyVaultFailure_NoFileFallback()
    {
        WriteSettings(new Dictionary<string, object>
        {
            ["Encrypt"] = false,
            ["ConnectionString"] = "Server=file;Database=d",
            ["Secrets"] = new Dictionary<string, string> { ["ERP-KEY"] = "file" }
        });
        VaultThrows($"{Env}-Secret-ERP-KEY", new RequestFailedException(503, "down"));
        VaultThrows($"{Env}-ConnectionString", new RequestFailedException(503, "down"));
        var provider = Provider(withVault: true);

        Assert.Null(await provider.GetSecretAsync(Env, "ERP-KEY", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.LoadEnvironmentOrThrowAsync(Env));
    }

    [Fact]
    public async Task KeyVault_CachesValueAndUsesOneClient()
    {
        WriteSettings(new Dictionary<string, object> { ["Encrypt"] = false, ["ConnectionString"] = "Server=file;Database=d" });
        VaultReturns($"{Env}-Secret-ERP-KEY", "vault");
        var provider = Provider(withVault: true);

        for (var i = 0; i < 3; i++)
        {
            await provider.GetSecretAsync(Env, "ERP-KEY", TestContext.Current.CancellationToken);
            await provider.LoadEnvironmentOrThrowAsync(Env);
        }

        _vault.Verify(c => c.GetSecretAsync($"{Env}-Secret-ERP-KEY", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _vault.Verify(c => c.GetSecretAsync($"{Env}-ConnectionString", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
