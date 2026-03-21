namespace PortwayApi.Services.Configuration;

/// <summary>
/// Tracks the last successful configuration reload for endpoints and environments.
/// Exposed via /health to let ops detect stale config.
/// </summary>
public class ReloadTracker
{
    private long _lastEndpointReloadTicks;
    private long _lastEnvironmentReloadTicks;

    public DateTime? LastEndpointReload
        => _lastEndpointReloadTicks == 0 ? null
         : new DateTime(Interlocked.Read(ref _lastEndpointReloadTicks), DateTimeKind.Utc);

    public DateTime? LastEnvironmentReload
        => _lastEnvironmentReloadTicks == 0 ? null
         : new DateTime(Interlocked.Read(ref _lastEnvironmentReloadTicks), DateTimeKind.Utc);

    public void RecordEndpointReload()
        => Interlocked.Exchange(ref _lastEndpointReloadTicks, DateTime.UtcNow.Ticks);

    public void RecordEnvironmentReload()
        => Interlocked.Exchange(ref _lastEnvironmentReloadTicks, DateTime.UtcNow.Ticks);
}
