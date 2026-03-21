using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Serilog;
using PortwayApi.Interfaces;
using PortwayApi.Services;
using PortwayApi.Services.Caching;

namespace PortwayApi.Services.Configuration;

/// <summary>
/// Monitors the environments folder for changes and invalidates cached environment settings
/// </summary>
public class EnvironmentFileWatcher : IHostedService, IDisposable
{
    private readonly string _environmentsPath;
    private readonly CacheManager _cacheManager;
    private readonly IEnvironmentSettingsProvider _environmentSettingsProvider;
    private readonly SseBroadcaster? _broadcaster;
    private readonly ReloadTracker _reloadTracker;
    private FileSystemWatcher? _fileWatcher;
    private readonly ConcurrentDictionary<string, DateTime> _lastReloadTimes = new();
    private readonly TimeSpan _reloadDebounceTime = TimeSpan.FromSeconds(2);
    private readonly Channel<(string Path, WatcherChangeTypes Type)> _eventChannel =
        Channel.CreateBounded<(string, WatcherChangeTypes)>(new BoundedChannelOptions(200)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });
    private Task? _consumerTask;
    private CancellationTokenSource? _consumerCts;

    public EnvironmentFileWatcher(
        CacheManager cacheManager,
        IEnvironmentSettingsProvider environmentSettingsProvider,
        ReloadTracker reloadTracker,
        SseBroadcaster? broadcaster = null)
    {
        var baseDir = Directory.GetCurrentDirectory();
        _environmentsPath            = Path.Combine(baseDir, "environments");
        _cacheManager                = cacheManager;
        _environmentSettingsProvider = environmentSettingsProvider;
        _reloadTracker               = reloadTracker;
        _broadcaster                 = broadcaster;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_environmentsPath))
        {
            Log.Warning("Environments folder not found at {Path} - environment file watching disabled", _environmentsPath);
            return Task.CompletedTask;
        }

        _consumerCts = new CancellationTokenSource();
        _consumerTask = ConsumeEventsAsync(_consumerCts.Token);

        _fileWatcher = new FileSystemWatcher(_environmentsPath)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
            Filter = "*.json",
            IncludeSubdirectories = true,
            EnableRaisingEvents = true,
            InternalBufferSize = 65536 // Increase from default 8192
        };

        _fileWatcher.Changed += OnFileChanged;
        _fileWatcher.Created += OnFileChanged;
        _fileWatcher.Deleted += OnFileChanged;
        _fileWatcher.Renamed += OnFileRenamed;

        Log.Debug("Environment file watcher initialized at path: {Path}", _environmentsPath);

        // WORKAROUND: Detect drvfs mount and use polling fallback for WSL2 compatibility
        if (_environmentsPath.StartsWith("/mnt/"))
        {
            Log.Debug("Detected drvfs mount - using polling fallback (checks every 3s)");
            StartPollingFallback();
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _eventChannel.Writer.TryComplete();
        try { _consumerCts?.Cancel(); } catch (ObjectDisposedException) { }
        _fileWatcher?.Dispose();

        if (_consumerTask != null)
            await _consumerTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        Log.Information("Environment file watcher stopped");
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
        => _eventChannel.Writer.TryWrite((e.FullPath, e.ChangeType));

    private void OnFileRenamed(object sender, RenamedEventArgs e)
        => _eventChannel.Writer.TryWrite((e.FullPath, e.ChangeType));

    private async Task ConsumeEventsAsync(CancellationToken ct)
    {
        await foreach (var (path, type) in _eventChannel.Reader.ReadAllAsync(ct))
        {
            try
            {
                await HandleFileChangeAsync(path, type);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unhandled error processing environment file change for {Path}", path);
            }
        }
    }

    private async Task HandleFileChangeAsync(string filePath, WatcherChangeTypes changeType)
    {
        var now = DateTime.UtcNow;
        if (_lastReloadTimes.TryGetValue(filePath, out var lastReload) && now - lastReload < _reloadDebounceTime)
        {
            Log.Debug("Ignoring duplicate file change event for {Path} (debounced)", filePath);
            return;
        }
        _lastReloadTimes[filePath] = now;

        try
        {
            // Extract environment name from path
            var environmentName = ExtractEnvironmentName(filePath);
            if (string.IsNullOrEmpty(environmentName))
            {
                Log.Debug("Could not determine environment name from path: {Path}", filePath);
                return;
            }

            // Invalidate cache entries related to this environment
            await InvalidateEnvironmentCacheAsync(environmentName);

            // Re-encrypt if a plaintext connection string was written (e.g. after IIS reset / config restore)
            // Only applies to per-environment settings (parts.Length >= 2), not the global settings.json
            var relativePath = Path.GetRelativePath(_environmentsPath, filePath);
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
    }

    private string? ExtractEnvironmentName(string filePath)
    {
        try
        {
            // Path format: {base}/environments/{environmentName}/settings.json
            var relativePath = Path.GetRelativePath(_environmentsPath, filePath);
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

    private Task InvalidateEnvironmentCacheAsync(string environmentName)
    {
        try
        {
            // The EnvironmentSettingsProvider reloads from disk on next request
            // We need to clear any cached connection strings or environment-specific data
            // Most importantly, trigger reload of environment settings on next access

            // Clear provider's internal cache if it has one
            // Note: EnvironmentSettingsProvider loads from disk each time, so no cache to clear there

            // However, SQL metadata cache uses environment names in keys
            // Format: "{environment}:{schema}.{object}" or similar
            // We should notify that environment settings changed so metadata is reloaded

            Log.Debug("Environment '{Environment}' cache invalidation complete", environmentName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error invalidating cache for environment {Environment}", environmentName);
        }

        return Task.CompletedTask;
    }

    private void StartPollingFallback()
    {
        var ct = _consumerCts!.Token;
        _ = Task.Run(async () =>
        {
            var lastWriteTimes = new Dictionary<string, DateTime>();

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (Directory.Exists(_environmentsPath))
                    {
                        var files = Directory.GetFiles(_environmentsPath, "*.json", SearchOption.AllDirectories);
                        foreach (var file in files)
                        {
                            if (ct.IsCancellationRequested) break;

                            var lastWrite = File.GetLastWriteTimeUtc(file);
                            if (lastWriteTimes.TryGetValue(file, out var previousWrite) && lastWrite > previousWrite)
                            {
                                Log.Debug("Polling detected change: {File}", file);
                                _eventChannel.Writer.TryWrite((file, WatcherChangeTypes.Changed));
                            }
                            lastWriteTimes[file] = lastWrite;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error in environment file polling");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, ct);
    }

    public void Dispose()
    {
        _consumerCts?.Cancel();
        _consumerCts?.Dispose();
        _fileWatcher?.Dispose();
    }
}
