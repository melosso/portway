using PortwayApi.Helpers;
using Xunit;

namespace PortwayApi.Tests.Helpers;

/// <summary>
/// PORTWAY_ environment variables land under their configuration keys
/// </summary>
public sealed class EnvAliasesTests
{
    private static Dictionary<string, string?> Resolve(params (string Name, string Value)[] env) =>
        EnvAliases.Resolve(name => env.FirstOrDefault(e => e.Name == name).Value);

    [Fact]
    public void ScalarAliasMapsToConfigKey()
    {
        var config = Resolve(("PORTWAY_TELEMETRY_PROVIDER", "Otlp"));

        Assert.Equal("Otlp", config["Telemetry:Provider"]);
    }

    [Fact]
    public void ListAliasSplitsIntoIndexedKeys()
    {
        var config = Resolve(("PORTWAY_PUBLIC_ORIGINS", "https://a.example.com, https://b.example.com,"));

        Assert.Equal("https://a.example.com", config["WebUi:PublicOrigins:0"]);
        Assert.Equal("https://b.example.com", config["WebUi:PublicOrigins:1"]);
        Assert.Equal(2, config.Count);
    }

    [Fact]
    public void DirectReadAliasIsNotAConfigKey()
    {
        var config = Resolve(("PORTWAY_USE_HTTPS", "true"));

        Assert.Empty(config);
    }
}
