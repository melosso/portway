using Microsoft.Extensions.Configuration;
using PortwayApi.Services.Configuration;
using Xunit;

namespace PortwayApi.Tests.Services;

/// <summary>
/// The deprecated WebUi section answers for Console with the same source precedence
/// </summary>
public sealed class ConsoleSectionAliasTests
{
    private static ConfigurationManager Build(params Dictionary<string, string?>[] sourcesLowToHigh)
    {
        var config = new ConfigurationManager();
        foreach (var source in sourcesLowToHigh)
            config.AddInMemoryCollection(source);
        config.AddConsoleSectionAlias();
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

        Assert.Equal(["https://a.example.com", "https://b.example.com"], config.GetSection("Console:PublicOrigins").Get<string[]>());
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
        Assert.Equal("Console:Customization:PromoText", ConsoleSectionAlias.Canonical("WebUi:Customization:PromoText"));
        Assert.Equal("Console:Enabled", ConsoleSectionAlias.Canonical("webui:Enabled"));
        Assert.Equal("Caching:Enabled", ConsoleSectionAlias.Canonical("Caching:Enabled"));
    }
}
