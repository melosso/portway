namespace PortwayApi.Services;

using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Serilog;

/// <summary>
/// Refreshes the health cache on an interval and broadcasts it to SSE clients
/// </summary>
public class HealthRefreshService : BackgroundService
{
    private readonly HealthCheckService _healthService;
    private readonly TimeSpan _interval;
    private readonly SseBroadcaster? _broadcaster;
    private readonly IHostApplicationLifetime? _lifetime;

    public HealthRefreshService(
        HealthCheckService healthService,
        TimeSpan interval,
        SseBroadcaster? broadcaster = null,
        IHostApplicationLifetime? lifetime = null)
    {
        _healthService = healthService;
        _interval = interval;
        _broadcaster = broadcaster;
        _lifetime = lifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for the server to start before the first check
        await WaitForApplicationStartedAsync(stoppingToken);

        if (stoppingToken.IsCancellationRequested)
            return;

        // Run an initial check so the cache is warm before the first request
        await RefreshAsync(stoppingToken);

        using var timer = new PeriodicTimer(_interval);
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshAsync(stoppingToken);
        }
    }

    private Task WaitForApplicationStartedAsync(CancellationToken stoppingToken)
    {
        if (_lifetime is null)
            return Task.CompletedTask;

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        if (_lifetime.ApplicationStarted.IsCancellationRequested)
        {
            tcs.TrySetResult();
            return tcs.Task;
        }

        _lifetime.ApplicationStarted.Register(
            static s => ((TaskCompletionSource)s!).TrySetResult(), tcs);

        stoppingToken.Register(() => tcs.TrySetCanceled(stoppingToken));

        return tcs.Task;
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        try
        {
            var report = await _healthService.CheckHealthAsync(ct);
            _broadcaster?.Broadcast("health", JsonSerializer.Serialize(new { status = report.Status.ToString() }));
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            Log.Warning("Background health refresh failed: {Error}", ex.Message);
        }
    }
}
