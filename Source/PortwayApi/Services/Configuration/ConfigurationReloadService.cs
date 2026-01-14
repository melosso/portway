using Microsoft.Extensions.Options;
using Serilog;
using PortwayApi.Services.Caching;

namespace PortwayApi.Services.Configuration;

/// <summary>
/// Monitors configuration changes and handles cache invalidation
/// </summary>
public class ConfigurationReloadService : IHostedService, IDisposable
{
    private readonly IOptionsMonitor<CacheOptions> _cacheOptionsMonitor;
    private readonly CacheManager _cacheManager;
    private readonly IDisposable? _cacheOptionsChangeToken;
    private readonly SemaphoreSlim _reloadSemaphore = new(1, 1);
    private DateTime _lastReloadTime = DateTime.MinValue;
    private readonly TimeSpan _reloadDebounceTime = TimeSpan.FromMilliseconds(500);

    public ConfigurationReloadService(
        IOptionsMonitor<CacheOptions> cacheOptionsMonitor,
        CacheManager cacheManager)
    {
        _cacheOptionsMonitor = cacheOptionsMonitor;
        _cacheManager = cacheManager;

        // Subscribe to configuration changes
        _cacheOptionsChangeToken = _cacheOptionsMonitor.OnChange(OnCacheConfigurationChanged);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Log.Information("Configuration reload enabled: Monitoring `appsettings.json` for changes");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Log.Information("Configuration reload service stopped");
        return Task.CompletedTask;
    }

    private void OnCacheConfigurationChanged(CacheOptions newOptions, string? name)
    {
        // Debounce: Prevent multiple rapid notifications from file save operations
        if (!_reloadSemaphore.Wait(0))
        {
            // Another thread is processing, skip this event
            return;
        }

        try
        {
            var now = DateTime.UtcNow;
            if (now - _lastReloadTime < _reloadDebounceTime)
            {
                Log.Debug("Ignoring duplicate configuration change event (debounced)");
                return;
            }
            _lastReloadTime = now;

            Log.Information("Cache configuration changed, new settings will be applied on next cache operation");
            Log.Debug("Cache enabled: {Enabled}, Provider: {Provider}, Default duration: {Duration}s",
                newOptions.Enabled,
                newOptions.ProviderType,
                newOptions.DefaultCacheDurationSeconds);

            // Note: Cache entries are not cleared here to prevent data loss
            // The new settings will be used for future cache operations
            // If you want to clear cache on config change, add: await _cacheManager.ClearAllAsync();
        }
        finally
        {
            _reloadSemaphore.Release();
        }
    }

    public void Dispose()
    {
        _cacheOptionsChangeToken?.Dispose();
        _reloadSemaphore?.Dispose();
    }
}
