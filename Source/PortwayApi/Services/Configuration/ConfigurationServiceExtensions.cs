using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;

namespace PortwayApi.Services.Configuration;

public static class ConfigurationServiceExtensions
{
    public static IServiceCollection AddPortwayConfigurationReload(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<ReloadTracker>();
        services.AddSingleton<ConfigAuditService>();
        services.AddSingleton<SettingsWriteService>();
        services.AddHostedService<ConfigurationReloadService>();
        services.AddHostedService<EnvironmentFileWatcher>();
        services.AddHostedService<EndpointFileWatcher>();
        services.Configure<EndpointReloadingOptions>(configuration.GetSection("EndpointReloading"));
        return services;
    }

    /// <summary>
    /// Adds appsettings.overrides.json with lower precedence than unprefixed environment variables
    /// </summary>
    public static IConfigurationBuilder AddConsoleOverrides(this IConfigurationBuilder config)
    {
        var source = new JsonConfigurationSource
        {
            Path = SettingsWriteService.OverridesFileName,
            Optional = true,
            ReloadOnChange = true
        };

        var envIndex = -1;
        for (var i = 0; i < config.Sources.Count; i++)
        {
            if (config.Sources[i] is EnvironmentVariablesConfigurationSource { Prefix: null or "" })
                envIndex = i;
        }

        if (envIndex < 0) config.Sources.Add(source);
        else config.Sources.Insert(envIndex, source);
        return config;
    }
}
