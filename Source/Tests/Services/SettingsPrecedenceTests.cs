using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.Memory;
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
}
