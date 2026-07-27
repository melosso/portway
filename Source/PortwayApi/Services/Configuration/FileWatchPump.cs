using System.Collections.Concurrent;
using System.Threading.Channels;
using Serilog;

namespace PortwayApi.Services.Configuration;

/// <summary>Watches a folder of JSON config files and feeds debounced change events to a single consumer</summary>
public abstract class FileWatchPump : IHostedService, IDisposable
{
    private readonly Channel<(string Path, WatcherChangeTypes Type)> _eventChannel =
        Channel.CreateBounded<(string, WatcherChangeTypes)>(new BoundedChannelOptions(200)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });

    private readonly ConcurrentDictionary<string, DateTime> _lastReloadTimes = new();
    private FileSystemWatcher? _fileWatcher;
    private Task? _consumerTask;
    private CancellationTokenSource? _consumerCts;
    private bool _disposed;

    protected FileWatchPump(string watchPath, string description)
    {
        WatchPath = watchPath;
        Description = description;
    }

    /// <summary>Absolute folder the watcher observes</summary>
    protected string WatchPath { get; }

    /// <summary>Human-readable name used in log messages, for example "Endpoint" or "Environment"</summary>
    protected string Description { get; }

    /// <summary>How long to ignore repeat events for the same file; re-read per event so config changes take effect</summary>
    protected virtual TimeSpan DebounceTime => TimeSpan.FromSeconds(2);

    /// <summary>Return false to skip starting the watcher entirely, for example when hot-reload is disabled</summary>
    protected virtual bool ShouldStart() => true;

    /// <summary>Handles one debounced change event</summary>
    protected abstract Task HandleFileChangeAsync(string filePath, WatcherChangeTypes changeType);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!ShouldStart())
            return Task.CompletedTask;

        if (!Directory.Exists(WatchPath))
        {
            Log.Warning("{Description} folder not found at {Path} - file watching disabled", Description, WatchPath);
            return Task.CompletedTask;
        }

        _consumerCts = new CancellationTokenSource();
        _consumerTask = ConsumeEventsAsync(_consumerCts.Token);

        _fileWatcher = new FileSystemWatcher(WatchPath)
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
        _fileWatcher.Renamed += OnFileChanged;

        Log.Debug("{Description} file watcher initialized at path: {Path}", Description, WatchPath);

        // WORKAROUND: Detect drvfs mount and use polling fallback for WSL2 compatibility
        if (WatchPath.StartsWith("/mnt/"))
        {
            Log.Debug("Detected drvfs mount - using polling fallback (checks every 3s)");
            StartPollingFallback();
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _eventChannel.Writer.TryComplete();
        if (!_disposed)
            _consumerCts?.Cancel();

        _fileWatcher?.Dispose();

        if (_consumerTask != null)
            await _consumerTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        Log.Information("{Description} file watcher stopped", Description);
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
        => _eventChannel.Writer.TryWrite((e.FullPath, e.ChangeType));

    private async Task ConsumeEventsAsync(CancellationToken ct)
    {
        await foreach (var (path, type) in _eventChannel.Reader.ReadAllAsync(ct))
        {
            if (IsDebounced(path))
                continue;

            try
            {
                await HandleFileChangeAsync(path, type);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unhandled error processing {Description} file change for {Path}", Description, path);
            }
        }
    }

    private bool IsDebounced(string filePath)
    {
        var now = DateTime.UtcNow;
        if (_lastReloadTimes.TryGetValue(filePath, out var lastReload) && now - lastReload < DebounceTime)
        {
            Log.Debug("Ignoring duplicate file change event for {Path} (debounced)", filePath);
            return true;
        }

        _lastReloadTimes[filePath] = now;
        return false;
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
                    if (Directory.Exists(WatchPath))
                    {
                        var files = Directory.GetFiles(WatchPath, "*.json", SearchOption.AllDirectories);
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
                    Log.Error(ex, "Error in {Description} file polling", Description);
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
        if (_disposed) return;
        _disposed = true;

        _consumerCts?.Cancel();
        _consumerCts?.Dispose();
        _fileWatcher?.Dispose();
        GC.SuppressFinalize(this);
    }
}
