using PortwayApi.Classes.Configuration;

namespace PortwayApi.Services.Configuration;

public static class ConfigurationServiceExtensions
{
    public static IServiceCollection AddPortwayConfigurationReload(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<ReloadTracker>();
        services.AddHostedService<ConfigurationReloadService>();
        services.AddHostedService<EnvironmentFileWatcher>();
        services.AddHostedService<EndpointFileWatcher>();
        services.Configure<EndpointReloadingOptions>(configuration.GetSection("EndpointReloading"));
        return services;
    }
}
