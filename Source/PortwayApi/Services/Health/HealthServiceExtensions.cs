using PortwayApi.Classes;
using PortwayApi.Interfaces;
using PortwayApi.Services.Providers;

using MsHealthCheckService = Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService;

namespace PortwayApi.Services.Health;

public static class HealthServiceExtensions
{
    public static IServiceCollection AddPortwayHealthServices(this IServiceCollection services)
    {
        services.AddHealthChecks();

        services.AddSingleton<PortwayApi.Services.HealthCheckService>(sp =>
            new PortwayApi.Services.HealthCheckService(
                sp.GetRequiredService<MsHealthCheckService>(),
                TimeSpan.FromSeconds(90),
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<IEnvironmentSettingsProvider>(),
                sp.GetRequiredService<EnvironmentSettings>(),
                sp.GetRequiredService<ISqlProviderFactory>()));

        services.AddSingleton<PortwayApi.Services.SseBroadcaster>();
        services.AddSingleton<PortwayApi.Services.MetricsService>();
        services.AddHostedService<PortwayApi.Services.MetricsPersistenceService>();

        services.AddHostedService(sp =>
            new PortwayApi.Services.HealthRefreshService(
                sp.GetRequiredService<PortwayApi.Services.HealthCheckService>(),
                TimeSpan.FromSeconds(60),
                sp.GetRequiredService<PortwayApi.Services.SseBroadcaster>(),
                sp.GetRequiredService<IHostApplicationLifetime>()));

        return services;
    }
}
