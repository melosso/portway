using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.Memory;
using PortwayApi.Helpers;
using PortwayApi.Services.Configuration;
using Xunit;

namespace PortwayApi.Tests.Services;

/// <summary>
/// Environment variables override appsettings.overrides.json. appsettings.overrides.json overrides appsettings.json.
/// </summary>
public sealed class SettingsPrecedenceTests
{
    [Fact]
    public void OverridesSourceIsPlacedBelowEnvironmentVariables()
    {
        var builder = new ConfigurationBuilder();
        builder.Add(new MemoryConfigurationSource());
        builder.AddEnvironmentVariables("DOTNET_");
        builder.Add(new MemoryConfigurationSource());
        builder.AddEnvironmentVariables();
        builder.AddCommandLine([]);

        builder.AddConsoleOverrides();

        var sources = builder.Sources;
        var overrides = sources.OfType<JsonConfigurationSource>().Single();
        var env = sources.OfType<EnvironmentVariablesConfigurationSource>().Single(s => string.IsNullOrEmpty(s.Prefix));
        Assert.Equal(sources.IndexOf(env) - 1, sources.IndexOf(overrides));
        Assert.True(overrides.ReloadOnChange);
        Assert.True(overrides.Optional);
    }

    [Fact]
    public void EnvironmentValueWinsOverConsoleOverrideAndConsoleWinsOverFile()
    {
        var dir = Directory.CreateTempSubdirectory("portway_prec_").FullName;
        Environment.SetEnvironmentVariable("PORTWAYPRECEDENCETEST__Value", "env");
        try
        {
            File.WriteAllText(Path.Combine(dir, SettingsWriteService.OverridesFileName),
                """{ "PORTWAYPRECEDENCETEST": { "Value": "console", "Other": "console" } }""");

            var builder = new ConfigurationBuilder().SetBasePath(dir);
            builder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PORTWAYPRECEDENCETEST:Value"] = "file",
                ["PORTWAYPRECEDENCETEST:Other"] = "file"
            });
            builder.AddEnvironmentVariables();
            builder.AddConsoleOverrides();
            var config = builder.Build();

            Assert.Equal("env", config["PORTWAYPRECEDENCETEST:Value"]);
            Assert.Equal("console", config["PORTWAYPRECEDENCETEST:Other"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PORTWAYPRECEDENCETEST__Value", null);
            Directory.Delete(dir, true);
        }
    }

    // an environment list replaces the whole list, lower sources merge by index otherwise
    [Theory]
    [InlineData("PORTWAY_PUBLIC_ORIGINS", "https://x.example.com", "Console:PublicOrigins")]
    [InlineData("Console__PublicOrigins__0", "https://x.example.com", "Console:PublicOrigins")]
    [InlineData("PORTWAY_KNOWN_NETWORKS", "10.0.0.0/8", "ForwardedHeaders:KnownNetworks")]
    [InlineData("ForwardedHeaders__KnownProxies__0", "10.0.0.1", "ForwardedHeaders:KnownProxies")]
    public void EnvironmentListReplacesLowerLists(string name, string value, string key)
    {
        var config = Build(new() { [name] = value }, key);

        Assert.Equal([value], config.GetSection(key).Get<string[]>() ?? []);
    }

    [Fact]
    public void LowerListStaysWithoutEnvironmentList()
    {
        var config = Build([], "Console:PublicOrigins");

        Assert.Equal(["a", "b", "c"], config.GetSection("Console:PublicOrigins").Get<string[]>() ?? []);
    }

    [Fact]
    public void AliasWinsOverNativeFormOfAnyCase()
    {
        var config = Build(new() { ["PORTWAY_PUBLIC_ORIGINS"] = "https://x.example.com", ["console__publicorigins__0"] = "https://y.example.com" }, "Console:PublicOrigins");

        Assert.Equal(["https://x.example.com"], config.GetSection("Console:PublicOrigins").Get<string[]>() ?? []);
    }

    [Fact]
    public void EnvironmentMapEntryMergesWithLowerEntries()
    {
        var config = Build(new() { ["Serilog__MinimumLevel__Override__System"] = "Error" }, "Serilog:MinimumLevel:Override");

        Assert.Equal("a", config["Serilog:MinimumLevel:Override:0"]);
        Assert.Equal("Error", config["Serilog:MinimumLevel:Override:System"]);
    }

    private static IConfigurationRoot Build(Dictionary<string, string> env, string key)
    {
        var builder = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{key}:0"] = "a",
            [$"{key}:1"] = "b",
            [$"{key}:2"] = "c"
        });
        builder.AddInMemoryCollection(env.ToDictionary(e => e.Key.Replace("__", ":"), e => (string?)e.Value));
        EnvAliases.ApplyConfigAliases(builder, env);
        return builder.Build();
    }
}
