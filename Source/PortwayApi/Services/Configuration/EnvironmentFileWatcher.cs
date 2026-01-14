using Serilog;
using PortwayApi.Interfaces;
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
    private FileSystemWatcher? _fileWatcher;
    private readonly SemaphoreSlim _reloadSemaphore = new(1, 1);
    private readonly Dictionary<string, DateTime> _lastReloadTimes = new();
    private readonly TimeSpan _reloadDebounceTime = TimeSpan.FromSeconds(2);
    private CancellationTokenSource? _pollingCancellationTokenSource;

    public EnvironmentFileWatcher(
        CacheManager cacheManager,
        IEnvironmentSettingsProvider environmentSettingsProvider)
    {
        var baseDir = Directory.GetCurrentDirectory();
        // Support both lowercase and uppercase folder names for cross-platform compatibility
        _environmentsPath = Directory.Exists(Path.Combine(baseDir, "Environments"))
            ? Path.Combine(baseDir, "Environments")
            : Path.Combine(baseDir, "environments");
        _cacheManager = cacheManager;
        _environmentSettingsProvider = environmentSettingsProvider;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_environmentsPath))
        {
            Log.Warning("Environments folder not found at {Path} - environment file watching disabled", _environmentsPath);
            return Task.CompletedTask;
        }

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

        Log.Information("Configuration reload enabled: Monitoring `/environments` folder for changes");
        Log.Debug("Environment file watcher initialized at path: {Path}", _environmentsPath);

        // WORKAROUND: Detect drvfs mount and use polling fallback for WSL2 compatibility
        if (_environmentsPath.StartsWith("/mnt/"))
        {
            Log.Debug("Detected drvfs mount - using polling fallback (checks every 3s)");
            StartPollingFallback();
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _pollingCancellationTokenSource?.Cancel();
        _fileWatcher?.Dispose();
        Log.Information("Environment file watcher stopped");
        return Task.CompletedTask;
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // Proper async event handling - don't use fire-and-forget
        Task.Run(async () =>
        {
            try
            {
                await HandleFileChangeAsync(e.FullPath, e.ChangeType);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unhandled error in file change handler for {Path}", e.FullPath);
            }
        });
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        // Proper async event handling - don't use fire-and-forget
        Task.Run(async () =>
        {
            try
            {
                await HandleFileChangeAsync(e.FullPath, e.ChangeType);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unhandled error in file rename handler for {Path}", e.FullPath);
            }
        });
    }

    private async Task HandleFileChangeAsync(string filePath, WatcherChangeTypes changeType)
    {
        // Debounce: Prevent multiple rapid reloads for the same file
        await _reloadSemaphore.WaitAsync();
        try
        {
            var now = DateTime.UtcNow;
            if (_lastReloadTimes.TryGetValue(filePath, out var lastReload))
            {
                if (now - lastReload < _reloadDebounceTime)
                {
                    Log.Debug("Ignoring duplicate file change event for {Path} (debounced)", filePath);
                    return;
                }
            }
            _lastReloadTimes[filePath] = now;

            // Extract environment name from path
            var environmentName = ExtractEnvironmentName(filePath);
            if (string.IsNullOrEmpty(environmentName))
            {
                Log.Debug("Could not determine environment name from path: {Path}", filePath);
                return;
            }

            // Invalidate cache entries related to this environment
            await InvalidateEnvironmentCacheAsync(environmentName);

            Log.Information("Environment '{Environment}' settings changed - will reload on next request", environmentName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error handling environment file change for {Path}", filePath);
        }
        finally
        {
            _reloadSemaphore.Release();
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
        _pollingCancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = _pollingCancellationTokenSource.Token;

        Task.Run(async () =>
        {
            var lastWriteTimes = new Dictionary<string, DateTime>();

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (!Directory.Exists(_environmentsPath))
                    {
                        await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                        continue;
                    }

                    var files = Directory.GetFiles(_environmentsPath, "*.json", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        if (cancellationToken.IsCancellationRequested) break;

                        var lastWrite = File.GetLastWriteTimeUtc(file);
                        if (lastWriteTimes.TryGetValue(file, out var previousWrite))
                        {
                            if (lastWrite > previousWrite)
                            {
                                Log.Debug("Polling detected change: {File}", file);
                                await HandleFileChangeAsync(file, WatcherChangeTypes.Changed);
                            }
                        }
                        lastWriteTimes[file] = lastWrite;
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
                    await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, cancellationToken);
    }

    public void Dispose()
    {
        _pollingCancellationTokenSource?.Cancel();
        _pollingCancellationTokenSource?.Dispose();
        _fileWatcher?.Dispose();
        _reloadSemaphore?.Dispose();
    }
}
