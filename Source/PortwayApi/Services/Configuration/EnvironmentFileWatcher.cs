using System.Text.Json;
using Serilog;
using PortwayApi.Classes;
using PortwayApi.Interfaces;
using PortwayApi.Services;

namespace PortwayApi.Services.Configuration;

/// <summary>
/// Monitors the environments folder for changes and invalidates cached environment settings
/// </summary>
public class EnvironmentFileWatcher : FileWatchPump
{
    private readonly IEnvironmentSettingsProvider _environmentSettingsProvider;
    private readonly SseBroadcaster? _broadcaster;
    private readonly ReloadTracker _reloadTracker;
    private readonly EnvironmentSettings _environmentSettings;

    public EnvironmentFileWatcher(
        IEnvironmentSettingsProvider environmentSettingsProvider,
        ReloadTracker reloadTracker,
        EnvironmentSettings environmentSettings,
        SseBroadcaster? broadcaster = null)
        : base(Path.Combine(Directory.GetCurrentDirectory(), "environments"), "Environment")
    {
        _environmentSettingsProvider = environmentSettingsProvider;
        _environmentSettings = environmentSettings;
        _reloadTracker = reloadTracker;
        _broadcaster = broadcaster;
    }

    protected override Task HandleFileChangeAsync(string filePath, WatcherChangeTypes changeType)
    {
        try
        {
            if (IsGlobalSettings(filePath))
            {
                // a delete is skipped because Reload would recreate the file with default environments
                if (changeType != WatcherChangeTypes.Deleted)
                {
                    _environmentSettings.Reload();
                    _reloadTracker.RecordEnvironmentReload();
                    _broadcaster?.Broadcast("reload", JsonSerializer.Serialize(new { type = "environments" }));
                }
                return Task.CompletedTask;
            }

            // Extract environment name from path
            var environmentName = ExtractEnvironmentName(filePath);
            if (string.IsNullOrEmpty(environmentName))
            {
                Log.Debug("Could not determine environment name from path: {Path}", filePath);
                return Task.CompletedTask;
            }

            // Re-encrypt plaintext connection strings in per-environment settings
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

    private bool IsGlobalSettings(string filePath) =>
        Path.GetRelativePath(WatchPath, filePath).Equals("settings.json", StringComparison.OrdinalIgnoreCase);

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
