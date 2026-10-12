using System.Text.Json;
using Microsoft.Extensions.Configuration;
using PortwayApi.Services.Configuration;
using Xunit;

namespace PortwayApi.Tests.Services;

/// <summary>
/// The console write path refuses a key set by an environment variable
/// </summary>
[Collection("Integration")]
public sealed class SettingsWriteLockTests
{
    private static Dictionary<string, JsonElement> Changes(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    [Fact]
    public async Task LockedKeyIsRefusedAndNothingIsWritten()
    {
        var writer = new SettingsWriteService(["PORTWAY_MCP_ENABLED"]);
        var before = File.Exists(SettingsWriteService.OverridesPath) ? File.ReadAllText(SettingsWriteService.OverridesPath) : null;

        var result = await writer.ApplyAsync(Changes("""{ "Caching:Enabled": true, "Mcp:Enabled": true }"""));

        Assert.False(result.Ok);
        Assert.Equal("Mcp:Enabled", result.Field);
        Assert.Contains("PORTWAY_MCP_ENABLED", result.Error);
        var after = File.Exists(SettingsWriteService.OverridesPath) ? File.ReadAllText(SettingsWriteService.OverridesPath) : null;
        Assert.Equal(before, after);
    }

    [Fact]
    public void LockedByReportsTheVariable()
    {
        var writer = new SettingsWriteService(["Caching__Enabled"]);

        Assert.Equal("Caching__Enabled", writer.LockedBy("Caching:Enabled"));
        Assert.Null(writer.LockedBy("Mcp:Enabled"));
    }

    [Fact]
    public async Task WriteReloadsConfigurationWithoutAFileWatcher()
    {
        var path = SettingsWriteService.OverridesPath;
        var before = File.Exists(path) ? File.ReadAllText(path) : null;
        try
        {
            var root = new ConfigurationBuilder()
                .AddJsonFile(path, optional: true, reloadOnChange: false)
                .Build();
            var writer = new SettingsWriteService([], root);

            var result = await writer.ApplyAsync(Changes("""{ "OpenApi:Footer:Text": "reloaded" }"""));

            Assert.True(result.Ok);
            Assert.Equal("reloaded", root["OpenApi:Footer:Text"]);
        }
        finally
        {
            if (before is null) File.Delete(path);
            else File.WriteAllText(path, before);
        }
    }
}
