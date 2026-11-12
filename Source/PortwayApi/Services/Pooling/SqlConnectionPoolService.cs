using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using PortwayApi.Services.Providers;
using Serilog;

namespace PortwayApi.Services;

/// <summary>
/// Configures and manages SQL connection pooling. Provider-aware: delegates connection creation to the registered ISqlProvider for the detected database type
/// </summary>
public class SqlConnectionPoolService
{
    private readonly SqlPoolingOptions _poolingOptions;
    private readonly ISqlProviderFactory _providerFactory;
    private readonly ConcurrentDictionary<string, string> _connectionStringCache = new();

    public SqlConnectionPoolService(SqlPoolingOptions poolingOptions, ISqlProviderFactory providerFactory)
    {
        _poolingOptions = poolingOptions;
        _providerFactory = providerFactory;

        // Start from clean driver pools regardless of which providers this deployment uses
        SqlConnection.ClearAllPools();
        Npgsql.NpgsqlConnection.ClearAllPools();
        MySqlConnector.MySqlConnection.ClearAllPools();

        Log.Debug("Database Connection Pool initialized with Min: {MinPoolSize}, Max: {MaxPoolSize}, Timeout: {Timeout}s, CommandTimeout: {CommandTimeout}s, AppName: '{AppName}'",
            _poolingOptions.MinPoolSize, _poolingOptions.MaxPoolSize, _poolingOptions.ConnectionTimeout, _poolingOptions.CommandTimeout, _poolingOptions.ApplicationName);
    }

    /// <summary>
    /// Returns an optimized connection string for the detected provider
    /// </summary>
    public string OptimizeConnectionString(string connectionString)
    {
        if (_connectionStringCache.TryGetValue(connectionString, out var optimized))
            return optimized;

        var provider = _providerFactory.GetProvider(connectionString);
        var result = provider.OptimizeConnectionString(connectionString, _poolingOptions);
        _connectionStringCache[connectionString] = result;
        return result;
    }

    /// <summary>
    /// Creates an open-ready connection for the detected provider. Returns DbConnection to support all ADO.NET providers
    /// </summary>
    public DbConnection CreateConnection(string connectionString)
    {
        var optimized = OptimizeConnectionString(connectionString);
        var provider = _providerFactory.GetProvider(connectionString);
        return provider.CreateConnection(optimized);
    }
}

public static class SqlConnectionPoolingExtensions
{
    public static IServiceCollection AddSqlConnectionPooling(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new SqlPoolingOptions(
            MinPoolSize: configuration.GetValue<int>("SqlConnectionPooling:MinPoolSize", 5),
            MaxPoolSize: configuration.GetValue<int>("SqlConnectionPooling:MaxPoolSize", 100),
            ConnectionTimeout: configuration.GetValue<int>("SqlConnectionPooling:ConnectionTimeoutSeconds", 15),
            EnablePooling: configuration.GetValue<bool>("SqlConnectionPooling:Enabled", true),
            ApplicationName: configuration.GetValue<string>("SqlConnectionPooling:ApplicationName", "PortwayAPI")!,
            CommandTimeout: configuration.GetValue<int>("SqlConnectionPooling:CommandTimeoutSeconds", 30)
        );

        services.AddSingleton(options);

        services.AddSingleton<SqlConnectionPoolService>(sp => new SqlConnectionPoolService(
            options,
            sp.GetRequiredService<ISqlProviderFactory>()
        ));

        return services;
    }
}
