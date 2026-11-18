using Microsoft.Extensions.Configuration;
using PortwayApi.Services.Configuration;
using Xunit;

namespace PortwayApi.Tests.Services;

/// <summary>
/// Configuration names removed in v0.13.0 are not read and each is reported with its current name
/// </summary>
public sealed class ConfigAliasesTests
{
    private static ConfigurationManager Build(Dictionary<string, string?> values)
    {
        var config = new ConfigurationManager();
        config.AddInMemoryCollection(values);
        return config;
    }

    public static TheoryData<string, string> Renamed() => [.. ConfigAliases.RenamedKeys.Select(r => (r.Former, r.Current))];

    [Theory]
    [MemberData(nameof(Renamed))]
    public void RemovedKey_LogsErrorWithCurrentName(string former, string current)
    {
        var config = Build(new() { [former] = "1" });

        var message = Assert.Single(ConfigAliases.FormerNames(config));
        Assert.Contains(former, message);
        Assert.Contains($"rename it to {current}", message);
        Assert.Null(config[current]);
    }

    [Fact]
    public void FormerSection_IsReportedAndNotRead()
    {
        var config = Build(new() { ["WebUi:Enabled"] = "false" });

        var message = Assert.Single(ConfigAliases.FormerNames(config));
        Assert.Contains("rename it to Console", message);
        Assert.Null(config["Console:Enabled"]);
    }

    [Theory]
    [InlineData("Mcp:RequireAuthentication")]
    [InlineData("Mcp:AppsEnabled")]
    public void RemovedKey_IsReported(string key) =>
        Assert.Contains("no longer applies", Assert.Single(ConfigAliases.FormerNames(Build(new() { [key] = "true" }))));

    [Fact]
    public void CurrentNames_ReportNothing()
    {
        var config = Build(ConfigAliases.RenamedKeys.ToDictionary(r => r.Current, _ => (string?)"1"));

        Assert.Empty(ConfigAliases.FormerNames(config));
    }

    [Fact]
    public void ShippedAppSettings_UseNoFormerName()
    {
        var config = new ConfigurationManager();
        config.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));

        Assert.Empty(ConfigAliases.FormerNames(config));
    }
}
