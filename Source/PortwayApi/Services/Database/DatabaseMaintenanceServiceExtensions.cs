namespace PortwayApi.Services.Database;

public static class DatabaseMaintenanceServiceExtensions
{
    public static IServiceCollection AddPortwayDatabaseMaintenance(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatabaseMaintenanceOptions>(configuration.GetSection("DatabaseMaintenance"));
        // Singleton plus hosted registration so UI endpoints can read LastRunResults
        services.AddSingleton<DatabaseMaintenanceService>();
        services.AddHostedService(sp => sp.GetRequiredService<DatabaseMaintenanceService>());
        return services;
    }
}
