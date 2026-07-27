using System.Text.Json;
using Serilog;
using PortwayApi.Interfaces;
using PortwayApi.Services;

namespace PortwayApi.Services.Configuration;

/// <summary>Monitors the environments folder for changes and invalidates cached environment settings</summary>
public class EnvironmentFileWatcher : FileWatchPump
{
    private readonly IEnvironmentSettingsProvider _environmentSettingsProvider;
    private readonly SseBroadcaster? _broadcaster;
    private readonly ReloadTracker _reloadTracker;

    public EnvironmentFileWatcher(
        IEnvironmentSettingsProvider environmentSettingsProvider,
        ReloadTracker reloadTracker,
        SseBroadcaster? broadcaster = null)
        : base(Path.Combine(Directory.GetCurrentDirectory(), "environments"), "Environment")
    {
        _environmentSettingsProvider = environmentSettingsProvider;
        _reloadTracker               = reloadTracker;
        _broadcaster                 = broadcaster;
    }

    protected override Task HandleFileChangeAsync(string filePath, WatcherChangeTypes changeType)
    {
        try
        {
            // Extract environment name from path
            var environmentName = ExtractEnvironmentName(filePath);
            if (string.IsNullOrEmpty(environmentName))
            {
                Log.Debug("Could not determine environment name from path: {Path}", filePath);
                return Task.CompletedTask;
            }

            // Re-encrypt if a plaintext connection string was written (e.g. after IIS reset / config restore)
            // Only applies to per-environment settings (parts.Length >= 2), not the global settings.json
            var relativePath = Path.GetRelativePath(WatchPath, filePath);
            var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (parts.Length >= 2 && changeType != WatcherChangeTypes.Deleted)
                _environmentSettingsProvider.EncryptEnvironmentIfNeeded(environmentName);

            Log.Information("Environment '{Environment}' settings changed, definition will reload on next request", environmentName);
            _reloadTracker.RecordEnvironmentReload();
            _broadcaster?.Broadcast("reload", JsonSerializer.Serialize(new { type = "environments" }));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error handling environment file change for {Path}", filePath);
        }

        return Task.CompletedTask;
    }

    private string? ExtractEnvironmentName(string filePath)
    {
        try
        {
            // Path format: {base}/environments/{environmentName}/settings.json
            var relativePath = Path.GetRelativePath(WatchPath, filePath);
            var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (parts.Length >= 2)
            {
                return parts[0]; // First part is the environment name
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error extracting environment name from {Path}", filePath);
        }

        return null;
    }
}
