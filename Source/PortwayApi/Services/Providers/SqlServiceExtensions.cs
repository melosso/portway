using PortwayApi.Classes;
using PortwayApi.Classes.Providers;
using PortwayApi.Interfaces;
using PortwayApi.Services.Files;

namespace PortwayApi.Services.Providers;

public static class SqlServiceExtensions
{
    public static IServiceCollection AddPortwaySqlServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Environment settings
        services.AddSingleton<IEnvironmentSettingsProvider, EnvironmentSettingsProvider>();
        services.AddSingleton<EnvironmentSettings>();

        // SQL providers
        services.AddSingleton<ISqlProvider, MsSqlProvider>();
        services.AddSingleton<ISqlProvider, PostgreSqlProvider>();
        services.AddSingleton<ISqlProvider, MySqlProvider>();
        services.AddSingleton<ISqlProvider, SqliteProvider>();
        services.AddSingleton<ISqlProviderFactory, SqlProviderFactory>();

        // OData/SQL services
        services.AddSingleton<IHostedService, PortwayApi.Services.StartupLogger>();
        services.AddSingleton<IEdmModelBuilder, EdmModelBuilder>();
        services.AddSingleton<IODataToSqlConverter, ODataToSqlConverter>();
        services.AddSingleton<PortwayApi.Services.SqlMetadataService>();
        services.AddHostedService<PortwayApi.Services.MetadataInitializationService>();
        services.AddSingleton<FileHandlerService>();

        // Connection pooling
        services.AddSqlConnectionPooling(configuration);

        return services;
    }
}
