using Microsoft.Extensions.Options;
using Serilog;
using PortwayApi.Services.Caching;

namespace PortwayApi.Services.Configuration;

/// <summary>Monitors configuration changes and handles cache invalidation</summary>
public class ConfigurationReloadService : IHostedService, IDisposable
{
    private readonly IOptionsMonitor<CacheOptions> _cacheOptionsMonitor;
    private readonly IDisposable? _cacheOptionsChangeToken;
    private long _lastReloadTicks = DateTime.MinValue.Ticks;
    private static readonly TimeSpan ReloadDebounceTime = TimeSpan.FromMilliseconds(500);

    public ConfigurationReloadService(IOptionsMonitor<CacheOptions> cacheOptionsMonitor)
    {
        _cacheOptionsMonitor = cacheOptionsMonitor;

        // Subscribe to configuration changes
        _cacheOptionsChangeToken = _cacheOptionsMonitor.OnChange(OnCacheConfigurationChanged);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Log.Information("Configuration reload service stopped");
        return Task.CompletedTask;
    }

    private void OnCacheConfigurationChanged(CacheOptions newOptions, string? name)
    {
        // Debounce: a single file save raises several notifications
        var now = DateTime.UtcNow;
        var previous = Interlocked.Exchange(ref _lastReloadTicks, now.Ticks);

        if (now - new DateTime(previous, DateTimeKind.Utc) < ReloadDebounceTime)
        {
            Log.Debug("Ignoring duplicate configuration change event (debounced)");
            return;
        }

        Log.Information("Cache configuration changed, new settings will be applied on next cache operation");
        Log.Debug("Cache enabled: {Enabled}, Provider: {Provider}, Default duration: {Duration}s",
            newOptions.Enabled,
            newOptions.ProviderType,
            newOptions.DefaultCacheDurationSeconds);

        // Cache entries are kept to prevent data loss; inject CacheManager and call ClearAllAsync to change that
    }

    public void Dispose()
    {
        _cacheOptionsChangeToken?.Dispose();
    }
}
