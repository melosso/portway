using System.Reflection;
using System.Text.Json;
using Moq;
using PortwayApi.Classes;
using PortwayApi.Interfaces;
using PortwayApi.Services.Configuration;
using PortwayApi.Tests.Support;
using Xunit;

namespace PortwayApi.Tests.Services;

/// <summary>
/// Edits to environments/settings.json on disk update the allowed environments without a restart
/// </summary>
public class EnvironmentFileWatcherTests
{
    private sealed class Watcher(EnvironmentSettings settings)
        : EnvironmentFileWatcher(new Mock<IEnvironmentSettingsProvider>().Object, new ReloadTracker(), settings)
    {
        public Task Change(string path, WatcherChangeTypes type) => HandleFileChangeAsync(path, type);
    }

    [Fact]
    public async Task GlobalSettingsChange_ReloadsAllowlist()
    {
        using var temp = new TempDirectory("envwatch");
        var settingsPath = temp.Combine("settings.json");
        void Write(params string[] envs) => File.WriteAllText(settingsPath, JsonSerializer.Serialize(new { Environment = new { ServerName = "s", AllowedEnvironments = envs } }));
        Write("prod");
        var settings = new EnvironmentSettings();
        typeof(EnvironmentSettings).GetField("_settingsPath", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(settings, settingsPath);
        settings.Reload();
        var globalPath = Path.Combine(Directory.GetCurrentDirectory(), "environments", "settings.json");

        Write("prod", "qa");
        await new Watcher(settings).Change(globalPath, WatcherChangeTypes.Changed);

        Assert.True(settings.IsEnvironmentAllowed("qa"));
    }
}
