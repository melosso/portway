using Microsoft.Extensions.Configuration;
using PortwayApi.Services.Configuration;
using Xunit;

namespace PortwayApi.Tests.Services;

/// <summary>
/// The deprecated WebUi section answers for Console with the same source precedence
/// </summary>
public sealed class ConfigAliasesTests
{
    private static ConfigurationManager Build(params Dictionary<string, string?>[] sourcesLowToHigh)
    {
        var config = new ConfigurationManager();
        foreach (var source in sourcesLowToHigh)
            config.AddInMemoryCollection(source);
        config.AddConfigAliases();
        return config;
    }

    [Fact]
    public void LegacyKeyAnswersForConsole()
    {
        var config = Build(new Dictionary<string, string?> { ["WebUi:Enabled"] = "false" });

        Assert.Equal("false", config["Console:Enabled"]);
    }

    [Fact]
    public void HigherSourceWinsWhicheverNameItUses()
    {
        var legacyEnvOverNewFile = Build(new Dictionary<string, string?> { ["Console:Enabled"] = "true" }, new Dictionary<string, string?> { ["WebUi:Enabled"] = "false" });
        var newEnvOverLegacyFile = Build(new Dictionary<string, string?> { ["WebUi:Enabled"] = "false" }, new Dictionary<string, string?> { ["Console:Enabled"] = "true" });

        Assert.Equal("false", legacyEnvOverNewFile["Console:Enabled"]);
        Assert.Equal("true", newEnvOverLegacyFile["Console:Enabled"]);
    }

    [Fact]
    public void ConsoleWinsWithinOneSource()
    {
        var config = Build(new Dictionary<string, string?> { ["WebUi:Enabled"] = "false", ["Console:Enabled"] = "true" });

        Assert.Equal("true", config["Console:Enabled"]);
    }

    [Fact]
    public void LegacyListsBindUnderConsole()
    {
        var config = Build(new Dictionary<string, string?> { ["WebUi:PublicOrigins:0"] = "https://a.example.com", ["WebUi:PublicOrigins:1"] = "https://b.example.com" });

        Assert.Equal(["https://a.example.com", "https://b.example.com"], config.GetSection("Console:PublicOrigins").Get<string[]>() ?? []);
        Assert.True(config.GetSection("Console").Exists());
    }

    [Fact]
    public void SourcesAddedAfterTheAliasAreRead()
    {
        var config = Build(new Dictionary<string, string?> { ["Console:SecureCookies"] = "false" });
        config.AddInMemoryCollection(new Dictionary<string, string?> { ["WebUi:SecureCookies"] = "true" });

        Assert.Equal("true", config["Console:SecureCookies"]);
    }

    [Fact]
    public void LegacyKeysWriteToConsole()
    {
        Assert.Equal("Console:Customization:PromoText", ConfigAliases.Canonical("WebUi:Customization:PromoText"));
        Assert.Equal("Console:Enabled", ConfigAliases.Canonical("webui:Enabled"));
        Assert.Equal("Caching:Enabled", ConfigAliases.Canonical("Caching:Enabled"));
    }

    [Fact]
    public void DeprecatedKeyAnswersForItsNewName()
    {
        var config = Build(new Dictionary<string, string?> { ["RateLimiting:IpWindow"] = "30", ["WebUi:Customization:EnableLandingPage"] = "false" });

        Assert.Equal("30", config["RateLimiting:IpWindowSeconds"]);
        Assert.Equal("false", config["Console:Customization:LandingPageEnabled"]);
    }

    [Fact]
    public void NewNameWinsWithinOneSourceAndHigherSourceWinsAcrossSources()
    {
        var same = Build(new Dictionary<string, string?> { ["Caching:ProviderType"] = "Redis", ["Caching:Provider"] = "Memory" });
        var oldOverNew = Build(new Dictionary<string, string?> { ["Caching:Provider"] = "Memory" }, new Dictionary<string, string?> { ["Caching:ProviderType"] = "Redis" });

        Assert.Equal("Memory", same["Caching:Provider"]);
        Assert.Equal("Redis", oldOverNew["Caching:Provider"]);
    }

    private sealed class ScalarShape
    {
        public string Theme { get; set; } = "";
        public bool HideModels { get; set; }
    }

    [Fact]
    public void FlatDeprecatedKeysBindIntoTheNewSection()
    {
        var config = Build(new Dictionary<string, string?> { ["OpenApi:ScalarTheme"] = "moon", ["OpenApi:ScalarHideModels"] = "true", ["RateLimiting:RedisConnectionString"] = "redis:6379" });

        var scalar = config.GetSection("OpenApi:Scalar").Get<ScalarShape>();

        Assert.Equal("moon", scalar?.Theme);
        Assert.True(scalar?.HideModels);
        Assert.Equal("redis:6379", config.GetSection("RateLimiting:Redis")["ConnectionString"]);
        Assert.Contains(config.GetSection("OpenApi").GetChildren(), c => c.Key == "Scalar");
    }

    [Fact]
    public void DeprecatedKeysWriteToTheirNewName()
    {
        Assert.Equal("RateLimiting:IpWindowSeconds", ConfigAliases.Canonical("ratelimiting:ipwindow"));
        Assert.Equal("Console:Customization:LandingPageEnabled", ConfigAliases.Canonical("WebUi:Customization:EnableLandingPage"));
        Assert.Equal("OpenApi:Scalar:Theme", ConfigAliases.Canonical("OpenApi:Scalar:Theme"));
    }

    [Fact]
    public void UnaliasedReadsDoNotAllocate()
    {
        var config = Build(new Dictionary<string, string?> { ["Oidc:Enabled"] = "true", ["WebUi:Enabled"] = "false" });
        _ = config["Oidc:Enabled"];

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1_000; i++) _ = config["Oidc:Enabled"];

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
