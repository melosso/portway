using Microsoft.Extensions.Options;
using Serilog;
using PortwayApi.Classes;
using PortwayApi.Classes.Configuration;
using PortwayApi.Services;

namespace PortwayApi.Services.Configuration;

/// <summary>
/// Monitors the endpoints folder for changes and invalidates endpoint/metadata caches
/// </summary>
public class EndpointFileWatcher : IHostedService, IDisposable
{
    private readonly string _endpointsPath;
    private readonly SqlMetadataService _sqlMetadataService;
    private readonly IOptionsMonitor<EndpointReloadingOptions> _optionsMonitor;
    private FileSystemWatcher? _fileWatcher;
    private readonly SemaphoreSlim _reloadSemaphore = new(1, 1);
    private readonly Dictionary<string, DateTime> _lastReloadTimes = new();
    private CancellationTokenSource? _pollingCancellationTokenSource;

    public EndpointFileWatcher(
        SqlMetadataService sqlMetadataService,
        IOptionsMonitor<EndpointReloadingOptions> optionsMonitor)
    {
        var baseDir = Directory.GetCurrentDirectory();
        // Support both lowercase and uppercase folder names for cross-platform compatibility
        _endpointsPath = Directory.Exists(Path.Combine(baseDir, "Endpoints"))
            ? Path.Combine(baseDir, "Endpoints")
            : Path.Combine(baseDir, "endpoints");
        _sqlMetadataService = sqlMetadataService;
        _optionsMonitor = optionsMonitor;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var options = _optionsMonitor.CurrentValue;

        if (!options.Enabled)
        {
            Log.Information("Endpoint hot-reload is DISABLED via configuration");
            return Task.CompletedTask;
        }

        if (!Directory.Exists(_endpointsPath))
        {
            Log.Warning("Endpoints folder not found at {Path} - endpoint file watching disabled", _endpointsPath);
            return Task.CompletedTask;
        }

        _fileWatcher = new FileSystemWatcher(_endpointsPath)
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

        Log.Information("Configuration reload enabled: Monitoring `/endpoints` folder for changes");
        Log.Debug("Endpoint file watcher initialized at path: {Path}", _endpointsPath);

        // WORKAROUND: Detect drvfs mount and use polling fallback for WSL2 compatibility
        if (_endpointsPath.StartsWith("/mnt/"))
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
        Log.Information("Endpoint file watcher stopped");
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
        var options = _optionsMonitor.CurrentValue;

        // Debounce: Prevent multiple rapid reloads for the same file
        await _reloadSemaphore.WaitAsync();
        try
        {
            var now = DateTime.UtcNow;
            var debounceTime = TimeSpan.FromMilliseconds(options.DebounceMs);

            if (_lastReloadTimes.TryGetValue(filePath, out var lastReload))
            {
                if (now - lastReload < debounceTime)
                {
                    Log.Debug("Ignoring duplicate file change event for {Path} (debounced)", filePath);
                    return;
                }
            }
            _lastReloadTimes[filePath] = now;

            // Extract endpoint type from path
            var endpointType = EndpointHandler.GetEndpointTypeFromPath(filePath);
            if (endpointType == null)
            {
                Log.Debug("Could not determine endpoint type from path: {Path}", filePath);
                return;
            }

            // Extract endpoint name from file path
            var endpointName = ExtractEndpointName(filePath);
            if (string.IsNullOrEmpty(endpointName))
            {
                Log.Debug("Could not determine endpoint name from path: {Path}", filePath);
                return;
            }

            // Reload endpoint definitions
            EndpointHandler.ReloadEndpointType(endpointType.Value);

            // Invalidate SQL metadata cache if it's a SQL endpoint
            if (endpointType == EndpointType.SQL)
            {
                // Use lazy reload strategy - clear cache, reload on next request
                _sqlMetadataService.ClearEndpointMetadata(endpointName);
                Log.Debug("SQL metadata cleared for endpoint '{Endpoint}'", endpointName);
            }

            Log.Information("Endpoint '{Name}' ({Type}) changed, will reload on next request", endpointName, endpointType);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error handling endpoint file change for {Path}", filePath);
        }
        finally
        {
            _reloadSemaphore.Release();
        }
    }

    private string? ExtractEndpointName(string filePath)
    {
        try
        {
            // For entity.json files, the endpoint name is the parent directory
            if (Path.GetFileName(filePath).Equals("entity.json", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFileName(Path.GetDirectoryName(filePath));
            }

            // For other JSON files, use the filename without extension
            return Path.GetFileNameWithoutExtension(filePath);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error extracting endpoint name from {Path}", filePath);
        }

        return null;
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
                    if (!Directory.Exists(_endpointsPath))
                    {
                        await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                        continue;
                    }

                    var files = Directory.GetFiles(_endpointsPath, "*.json", SearchOption.AllDirectories);
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
                    Log.Error(ex, "Error in endpoint file polling");
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
