using Microsoft.Extensions.Configuration;
using PortwayApi.Services.Configuration;
using Xunit;

namespace PortwayApi.Tests.Services;

/// <summary>
/// Lists and maps written by the console replace the base file, and encrypted values are read decrypted
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
}
