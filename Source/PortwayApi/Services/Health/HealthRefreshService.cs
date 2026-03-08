namespace PortwayApi.Services;

using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Serilog;

/// <summary>
/// Background service that proactively refreshes the health cache on a fixed interval.
/// This ensures /health always returns immediately from cache rather than blocking
/// on SQL and proxy connectivity checks.
/// After each refresh the result is broadcast to all connected SSE clients.
/// </summary>
public class HealthRefreshService : BackgroundService
{
    private readonly HealthCheckService _healthService;
    private readonly TimeSpan _interval;
    private readonly SseBroadcaster? _broadcaster;

    public HealthRefreshService(HealthCheckService healthService, TimeSpan interval, SseBroadcaster? broadcaster = null)
    {
        _healthService = healthService;
        _interval      = interval;
        _broadcaster   = broadcaster;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run an initial check immediately so the cache is warm before the first request.
        await RefreshAsync(stoppingToken);

        using var timer = new PeriodicTimer(_interval);
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshAsync(stoppingToken);
        }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        try
        {
            var report = await _healthService.CheckHealthAsync(ct);
            _broadcaster?.Broadcast("health", JsonSerializer.Serialize(new { status = report.Status.ToString() }));
        }
        catch (OperationCanceledException) { /* shutting down */ }
        catch (Exception ex)
        {
            Log.Warning("Background health refresh failed: {Error}", ex.Message);
        }
    }
}
