using System.Text.Json;
using Microsoft.Extensions.Configuration;
using System.Text.Json.Nodes;
using PortwayApi.Services.Configuration;
using Xunit;

namespace PortwayApi.Tests.Services;

/// <summary>
/// Every console-writable kind refuses values outside its rule before anything is written
/// </summary>
public sealed class SettingsWriteKindTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    private static string? Error(string key, string json) => SettingsWriteService.Coerce(key, Json(json)).Error;

    [Theory]
    [InlineData("\"../x\"")]
    [InlineData("\"a/../../b\"")]
    [InlineData("\"/etc/portway\"")]
    [InlineData("\"C:\\\\data\"")]
    [InlineData("\"endpoints/a\"")]
    [InlineData("\"tokens\"")]
    [InlineData("\".core/x\"")]
    [InlineData("\"auth.db\"")]
    [InlineData("\"data/appsettings.json\"")]
    [InlineData("\"\"")]
    public void RelativePath_RefusesEscapesAndPortwayFiles(string json) =>
        Assert.NotNull(Error("FileStorage:StorageDirectory", json));

    [Fact]
    public void RelativePath_AcceptsAFolderUnderTheWorkingDirectory() =>
        Assert.Null(Error("RequestTrafficLogging:LogDirectory", "\"log/traffic\""));

    [Fact]
    public void SqlitePath_MustBeADatabaseFile()
    {
        Assert.NotNull(Error("RequestTrafficLogging:SqlitePath", "\"log/traffic.txt\""));
        Assert.NotNull(Error("RequestTrafficLogging:SqlitePath", "\"metrics.db\""));
        Assert.Null(Error("RequestTrafficLogging:SqlitePath", "\"log/traffic_logs.db\""));
    }

    [Theory]
    [InlineData("\"/api/x\"")]
    [InlineData("\"/api\"")]
    [InlineData("\"/_\"")]
    [InlineData("\"/docs\"")]
    [InlineData("\"/health/live\"")]
    [InlineData("\"metrics\"")]
    [InlineData("\"/Metrics\"")]
    [InlineData("\"/\"")]
    public void Route_RefusesReservedAndMalformedPaths(string json) =>
        Assert.NotNull(Error("Telemetry:Prometheus:Path", json));

    [Fact]
    public void Route_AcceptsALowerCasePath() => Assert.Null(Error("Mcp:Path", "\"/ai/mcp\""));

    [Theory]
    [InlineData("\"\"", true)]
    [InlineData("\"/portway\"", true)]
    [InlineData("\"/a/b\"", true)]
    [InlineData("\"portway\"", false)]
    [InlineData("\"/..\"", false)]
    [InlineData("\"/a b\"", false)]
    public void PathBase(string json, bool ok) => Assert.Equal(ok, Error("PathBase", json) is null);

    [Fact]
    public void AllowedHosts_IsStoredAsOneSemicolonSeparatedString()
    {
        var (node, error) = SettingsWriteService.Coerce("AllowedHosts", Json("""["portway.example.com", "*.example.org", "[::1]"]"""));

        Assert.Null(error);
        Assert.Equal("portway.example.com;*.example.org;[::1]", node!.GetValue<string>());
    }

    [Theory]
    [InlineData("""[]""")]
    [InlineData("""["evil.com;*"]""")]
    [InlineData("""["http://host"]""")]
    public void AllowedHosts_RefusesEmptyAndMalformedEntries(string json) => Assert.NotNull(Error("AllowedHosts", json));

    [Fact]
    public void Extensions_MustStartWithADot()
    {
        Assert.Null(Error("FileStorage:BlockedExtensions", """[".exe", ".PS1"]"""));
        Assert.NotNull(Error("FileStorage:BlockedExtensions", """["exe"]"""));
        Assert.NotNull(Error("FileStorage:BlockedExtensions", """[".tar.gz"]"""));
    }

    [Fact]
    public void ContentTypes_MustBeMediaTypes()
    {
        Assert.Null(Error("Caching:CacheableContentTypes", """["application/json", "text/xml"]"""));
        Assert.NotNull(Error("Caching:CacheableContentTypes", """["json"]"""));
    }

    [Fact]
    public void EndpointDurations_CheckNamesAndRange()
    {
        Assert.Null(Error("Caching:EndpointCacheDurations", """{ "Products": 600 }"""));
        Assert.NotNull(Error("Caching:EndpointCacheDurations", """{ "Products": -1 }"""));
        Assert.NotNull(Error("Caching:EndpointCacheDurations", """{ "a:b": 10 }"""));
        Assert.NotNull(Error("Caching:EndpointCacheDurations", """[600]"""));
    }

    [Fact]
    public void LevelOverrides_CheckNamespacesAndLevels()
    {
        var (node, error) = SettingsWriteService.Coerce("Serilog:MinimumLevel:Override", Json("""{ "Microsoft.AspNetCore": "warning" }"""));

        Assert.Null(error);
        Assert.Equal("Warning", node!["Microsoft.AspNetCore"]!.GetValue<string>());
        Assert.NotNull(Error("Serilog:MinimumLevel:Override", """{ "Microsoft": "Loud" }"""));
        Assert.NotNull(Error("Serilog:MinimumLevel:Override", """{ "Microsoft:x": "Debug" }"""));
    }

    [Theory]
    [InlineData("\"x;Password=y\"")]
    [InlineData("\"x'\"")]
    [InlineData("\"x=y\"")]
    public void ApplicationName_RefusesConnectionStringSyntax(string json) =>
        Assert.NotNull(Error("SqlConnectionPooling:ApplicationName", json));

    [Fact]
    public void ResourceAttributes_MustBeKeyValuePairs()
    {
        Assert.Null(Error("Telemetry:ResourceAttributes", "\"deployment.environment=production,team=core\""));
        Assert.NotNull(Error("Telemetry:ResourceAttributes", "\"production\""));
    }

    [Fact]
    public void Number_ChecksTheRange()
    {
        Assert.Null(Error("DatabaseMaintenance:FreePageRatioThreshold", "0.3"));
        Assert.NotNull(Error("DatabaseMaintenance:FreePageRatioThreshold", "1.5"));
    }

    [Fact]
    public void Secret_IsStoredEncryptedAndEmptyClearsIt()
    {
        var (node, error) = SettingsWriteService.Coerce("Caching:Redis:ConnectionString", Json("\"redis:6379,password=hunter2\""));

        Assert.Null(error);
        Assert.StartsWith("PWENC:", node!.GetValue<string>());
        Assert.DoesNotContain("hunter2", node.GetValue<string>());
        Assert.Equal((null, null), SettingsWriteService.Coerce("Caching:Redis:ConnectionString", Json("\"\"")));
        Assert.NotNull(Error("Proxy:Password", "\"a\\nb\""));
    }

    [Theory]
    [InlineData("Console:SeedPassword")]
    [InlineData("Console:SeedToken")]
    public void SeedCredentials_CanOnlyBeCleared(string key)
    {
        Assert.NotNull(Error(key, "\"new-password\""));
        Assert.Null(Error(key, "\"\""));
    }

    [Fact]
    public async Task CrossCheck_UsesTheCurrentValueForTheOtherSide()
    {
        var root = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Mcp:MaxPageSize"] = "100", ["Telemetry:Prometheus:Path"] = "/metrics", ["FileStorage:StorageDirectory"] = "storage/files" })
            .Build();
        var writer = new SettingsWriteService([], root);

        var pageSize = await writer.ApplyAsync(new Dictionary<string, JsonElement> { ["Mcp:DefaultPageSize"] = Json("500") });
        var paths = await writer.ApplyAsync(new Dictionary<string, JsonElement> { ["Mcp:Path"] = Json("\"/metrics\"") });
        var logs = await writer.ApplyAsync(new Dictionary<string, JsonElement> { ["RequestTrafficLogging:LogDirectory"] = Json("\"storage/files/traffic\"") });
        var storage = await writer.ApplyAsync(new Dictionary<string, JsonElement> { ["FileStorage:StorageDirectory"] = Json("\"log/uploads\"") });

        Assert.Equal("Mcp:DefaultPageSize", pageSize.Field);
        Assert.Equal("Mcp:Path", paths.Field);
        Assert.Equal("RequestTrafficLogging:LogDirectory", logs.Field);
        Assert.Equal("FileStorage:StorageDirectory", storage.Field);
    }

    [Fact]
    public void LockedBy_ReportsAnEnvironmentVariableUnderTheDeprecatedName()
    {
        var writer = new SettingsWriteService(["RateLimiting__IpWindow"]);

        Assert.Equal("RateLimiting__IpWindow", writer.LockedBy("RateLimiting:IpWindowSeconds"));
    }

    [Fact]
    public void Migration_MovesDeprecatedKeysUnlessTheNewNameIsSet()
    {
        var root = JsonNode.Parse("""{ "RateLimiting": { "IpWindow": 30, "TokenWindow": 10, "TokenWindowSeconds": 20 }, "OpenApi": { "ScalarTheme": "moon" } }""")!.AsObject();

        SettingsWriteService.MigrateDeprecatedKeys(root);

        Assert.Equal(30, root["RateLimiting"]!["IpWindowSeconds"]!.GetValue<int>());
        Assert.Equal(20, root["RateLimiting"]!["TokenWindowSeconds"]!.GetValue<int>());
        Assert.Null(root["RateLimiting"]!["IpWindow"]);
        Assert.Null(root["RateLimiting"]!["TokenWindow"]);
        Assert.Equal("moon", root["OpenApi"]!["Scalar"]!["Theme"]!.GetValue<string>());
    }

    [Fact]
    public void EveryRenamedKeyIsWritableUnderItsNewNameOnly()
    {
        var keys = SettingsWriteService.Schema.Select(s => s.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.All(ConfigAliases.RenamedKeys, r => Assert.DoesNotContain(r.Deprecated, keys));
    }
}
