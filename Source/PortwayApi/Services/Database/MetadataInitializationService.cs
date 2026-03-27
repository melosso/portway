using Microsoft.Extensions.Hosting;
using PortwayApi.Classes;
using PortwayApi.Interfaces;
using Serilog;

namespace PortwayApi.Services;

/// <summary>
/// Background service that initializes the SQL metadata cache after the application
/// has started listening, so startup is never blocked by database connectivity.
/// </summary>
public sealed class MetadataInitializationService : BackgroundService
{
    private readonly SqlMetadataService _metadataService;
    private readonly EnvironmentSettings _environmentSettings;
    private readonly IEnvironmentSettingsProvider _environmentSettingsProvider;
    private readonly IHostApplicationLifetime _lifetime;

    public MetadataInitializationService(
        SqlMetadataService metadataService,
        EnvironmentSettings environmentSettings,
        IEnvironmentSettingsProvider environmentSettingsProvider,
        IHostApplicationLifetime lifetime)
    {
        _metadataService = metadataService;
        _environmentSettings = environmentSettings;
        _environmentSettingsProvider = environmentSettingsProvider;
        _lifetime = lifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait until the HTTP server is fully started before touching the database,
        // so the app is always ready to serve requests (health, UI, OpenAPI) immediately.
        await WaitForApplicationStartedAsync(stoppingToken);

        if (stoppingToken.IsCancellationRequested)
            return;

        Log.Information("Starting background SQL metadata initialization...");

        try
        {
            var sqlEndpoints = EndpointHandler.GetSqlEndpoints();

            await _metadataService.InitializeAsync(
                sqlEndpoints,
                _environmentSettings,
                async environment =>
                {
                    try
                    {
                        var (connectionString, _, _) = await _environmentSettingsProvider.LoadEnvironmentOrThrowAsync(environment);
                        return connectionString ?? string.Empty;
                    }
                    catch
                    {
                        return string.Empty;
                    }
                });

            Log.Information("SQL metadata initialization complete ({Count} endpoints cached)",
                _metadataService.GetCachedEndpoints().Count());
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            Log.Error(ex, "SQL metadata initialization failed: {Message}", ex.Message);
        }
    }

    private Task WaitForApplicationStartedAsync(CancellationToken stoppingToken)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Resolve immediately if already started (e.g. in tests where ApplicationStarted never fires)
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
}
