using Microsoft.Extensions.Configuration;
using PortwayApi.Services.Configuration;
using Xunit;

namespace PortwayApi.Tests.Services;

/// <summary>
/// Console overrides provider behaviour.
/// </summary>
public sealed class ConsoleOverridesProviderTests
{
    private static IConfigurationRoot Build(string baseJson, string overridesJson, Func<string, string?>? decrypt = null)
    {
        var dir = Directory.CreateTempSubdirectory("portway_overrides_").FullName;
        File.WriteAllText(Path.Combine(dir, "appsettings.json"), baseJson);
        File.WriteAllText(Path.Combine(dir, SettingsWriteService.OverridesFileName), overridesJson);

        var builder = new ConfigurationBuilder().SetBasePath(dir).AddJsonFile("appsettings.json");
        var source = new ConsoleOverridesConfigurationSource
        {
            Path = SettingsWriteService.OverridesFileName,
            Optional = true,
            Decrypt = decrypt ?? (_ => null)
        };
        source.ResolveFileProvider();
        builder.Add(source);
        return builder.Build();
    }

    [Fact]
    public void ShorterListReplacesTheBaseList()
    {
        var config = Build(
            """{ "Console": { "CorsOrigins": ["https://a", "https://b", "https://c", "https://d"] } }""",
            """{ "Console": { "CorsOrigins": ["https://x", "https://y"] } }""");

        Assert.Equal(["https://x", "https://y"], config.GetSection("Console:CorsOrigins").Get<string[]>() ?? []);
    }

    [Fact]
    public void EmptyListClearsTheBaseList()
    {
        var config = Build(
            """{ "FileStorage": { "BlockedExtensions": [".exe", ".dll"] } }""",
            """{ "FileStorage": { "BlockedExtensions": [] } }""");

        Assert.Empty(config.GetSection("FileStorage:BlockedExtensions").Get<string[]>() ?? []);
    }

    [Fact]
    public void MapEntryMissingFromTheOverrideIsGone()
    {
        var config = Build(
            """{ "Caching": { "EndpointCacheDurations": { "Products": 600, "Items": 600 } } }""",
            """{ "Caching": { "EndpointCacheDurations": { "Products": 60 } } }""");

        var map = config.GetSection("Caching:EndpointCacheDurations").Get<Dictionary<string, int>>();

        Assert.Equal(new Dictionary<string, int> { ["Products"] = 60 }, map);
    }

    [Fact]
    public void ListsNotWrittenByTheConsoleStillMergeAsBefore()
    {
        var config = Build(
            """{ "Caching": { "CacheableContentTypes": ["application/json", "text/xml"] }, "Other": ["a", "b"] }""",
            """{ "Other": ["c"] }""");

        Assert.Equal(["application/json", "text/xml"], config.GetSection("Caching:CacheableContentTypes").Get<string[]>() ?? []);
        Assert.Equal(["c", "b"], config.GetSection("Other").Get<string[]>() ?? []);
    }

    [Fact]
    public void EncryptedValueIsReadDecrypted()
    {
        var config = Build("{}", """{ "Caching": { "Redis": { "ConnectionString": "PWENC:abc::def" } } }""",
            value => value == "PWENC:abc::def" ? "redis:6379" : null);

        Assert.Equal("redis:6379", config["Caching:Redis:ConnectionString"]);
    }

    [Fact]
    public void UndecryptableValueIsIgnoredAndHidesTheBaseValue()
    {
        var config = Build(
            """{ "Caching": { "Redis": { "ConnectionString": "localhost:6379" } } }""",
            """{ "Caching": { "Redis": { "ConnectionString": "PWENC:broken" } } }""");

        Assert.Null(config["Caching:Redis:ConnectionString"]);
    }

    private static ConfigurationManager BuildWithAliases(string baseJson, string overridesJson, Dictionary<string, string?>? higherLayer = null)
    {
        var dir = Directory.CreateTempSubdirectory("portway_overrides_").FullName;
        File.WriteAllText(Path.Combine(dir, "appsettings.json"), baseJson);
        File.WriteAllText(Path.Combine(dir, SettingsWriteService.OverridesFileName), overridesJson);

        var config = new ConfigurationManager();
        config.SetBasePath(dir);
        config.AddJsonFile("appsettings.json");
        var source = new ConsoleOverridesConfigurationSource { Path = SettingsWriteService.OverridesFileName, Optional = true };
        source.ResolveFileProvider();
        ((IConfigurationBuilder)config).Add(source);
        if (higherLayer is not null) config.AddInMemoryCollection(higherLayer);
        config.AddConfigAliases();
        return config;
    }

    [Fact]
    public void ShorterListReplacesTheLegacySectionList()
    {
        var config = BuildWithAliases(
            """{ "WebUi": { "PublicOrigins": ["https://a", "https://removed", "https://also-removed"] } }""",
            """{ "Console": { "PublicOrigins": ["https://a"] } }""");

        Assert.Equal(["https://a"], config.GetSection("Console:PublicOrigins").Get<string[]>() ?? []);
    }

    [Fact]
    public void EmptyListClearsTheLegacySectionList()
    {
        var config = BuildWithAliases(
            """{ "WebUi": { "PublicOrigins": ["https://removed"] } }""",
            """{ "Console": { "PublicOrigins": [] } }""");

        Assert.Empty(config.GetSection("Console:PublicOrigins").Get<string[]>() ?? []);
    }

    [Fact]
    public void LegacyListStaysWithoutAnOverride()
    {
        var config = BuildWithAliases("""{ "WebUi": { "PublicOrigins": ["https://a", "https://b"] } }""", "{}");

        Assert.Equal(["https://a", "https://b"], config.GetSection("Console:PublicOrigins").Get<string[]>() ?? []);
    }

    [Fact]
    public void LegacyLayerAboveTheOverridesStillApplies()
    {
        var config = BuildWithAliases(
            """{ "WebUi": { "PublicOrigins": ["https://removed", "https://also-removed"] } }""",
            """{ "Console": { "PublicOrigins": ["https://a"] } }""",
            new() { ["WebUi:PublicOrigins:0"] = "https://env" });

        Assert.Equal(["https://env"], config.GetSection("Console:PublicOrigins").Get<string[]>() ?? []);
    }

    [Fact]
    public void LegacyAndCurrentListKeysKeepTheirOrder()
    {
        var items = string.Join(",", Enumerable.Range(0, 12).Select(i => $"\"https://{i}\""));
        var config = BuildWithAliases($$"""{ "WebUi": { "CorsOrigins": [{{items}}] } }""", "{}", new() { ["Console:CorsOrigins:0"] = "https://0" });

        Assert.Equal(Enumerable.Range(0, 12).Select(i => $"https://{i}"), config.GetSection("Console:CorsOrigins").Get<string[]>() ?? []);
    }
}
