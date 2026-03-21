using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
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
    private readonly SseBroadcaster? _broadcaster;
    private readonly ReloadTracker _reloadTracker;
    private FileSystemWatcher? _fileWatcher;
    private readonly ConcurrentDictionary<string, DateTime> _lastReloadTimes = new();
    private readonly Channel<(string Path, WatcherChangeTypes Type)> _eventChannel =
        Channel.CreateBounded<(string, WatcherChangeTypes)>(new BoundedChannelOptions(200)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });
    private Task? _consumerTask;
    private CancellationTokenSource? _consumerCts;

    public EndpointFileWatcher(
        SqlMetadataService sqlMetadataService,
        IOptionsMonitor<EndpointReloadingOptions> optionsMonitor,
        ReloadTracker reloadTracker,
        SseBroadcaster? broadcaster = null)
    {
        var baseDir = Directory.GetCurrentDirectory();
        _endpointsPath  = Path.Combine(baseDir, "endpoints");
        _sqlMetadataService = sqlMetadataService;
        _optionsMonitor = optionsMonitor;
        _reloadTracker  = reloadTracker;
        _broadcaster    = broadcaster;
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

        _consumerCts = new CancellationTokenSource();
        _consumerTask = ConsumeEventsAsync(_consumerCts.Token);

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

        Log.Debug("Endpoint file watcher initialized at path: {Path}", _endpointsPath);

        // WORKAROUND: Detect drvfs mount and use polling fallback for WSL2 compatibility
        if (_endpointsPath.StartsWith("/mnt/"))
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

        Log.Information("Endpoint file watcher stopped");
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
                Log.Error(ex, "Unhandled error processing file change for {Path}", path);
            }
        }
    }

    private async Task HandleFileChangeAsync(string filePath, WatcherChangeTypes changeType)
    {
        var options = _optionsMonitor.CurrentValue;
        var now = DateTime.UtcNow;
        var debounceTime = TimeSpan.FromMilliseconds(options.DebounceMs);

        if (_lastReloadTimes.TryGetValue(filePath, out var lastReload) && now - lastReload < debounceTime)
        {
            Log.Debug("Ignoring duplicate file change event for {Path} (debounced)", filePath);
            return;
        }
        _lastReloadTimes[filePath] = now;

        try
        {
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
                _sqlMetadataService.ClearEndpointMetadata(endpointName);
                Log.Debug("SQL metadata cleared for endpoint '{Endpoint}'", endpointName);
            }

            var ns = ExtractNamespace(filePath);
            if (ns != null)
                Log.Information("Endpoint '{Name}' ({Type}, namespace: {Namespace}) changed, will reload on next request", endpointName, endpointType, ns);
            else
                Log.Information("Endpoint '{Name}' ({Type}) changed, will reload on next request", endpointName, endpointType);
            _reloadTracker.RecordEndpointReload();
            _broadcaster?.Broadcast("reload", JsonSerializer.Serialize(new { type = "endpoints" }));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error handling endpoint file change for {Path}", filePath);
        }
    }

    private static readonly HashSet<string> _typeFolderNames = new(StringComparer.OrdinalIgnoreCase)
        { "SQL", "Proxy", "Static", "Files", "Webhooks" };

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

    private string? ExtractNamespace(string filePath)
    {
        try
        {
            if (!Path.GetFileName(filePath).Equals("entity.json", StringComparison.OrdinalIgnoreCase))
                return null;

            // .../endpoints/{Type}/{Namespace?}/{EndpointName}/entity.json
            var endpointDir  = Path.GetDirectoryName(filePath);          // EndpointName dir
            var namespaceDir = Path.GetDirectoryName(endpointDir);       // Namespace or Type dir
            var namespaceName = Path.GetFileName(namespaceDir);

            return namespaceName == null || _typeFolderNames.Contains(namespaceName) ? null : namespaceName;
        }
        catch
        {
            return null;
        }
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
                    if (Directory.Exists(_endpointsPath))
                    {
                        var files = Directory.GetFiles(_endpointsPath, "*.json", SearchOption.AllDirectories);
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
                    Log.Error(ex, "Error in endpoint file polling");
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
