using System;
using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PortwayApi.Services.Providers;
using Serilog;

namespace PortwayApi.Services;

/// <summary>Configures and manages SQL connection pooling. Provider-aware: delegates connection creation to the registered ISqlProvider for the detected database type</summary>
public class SqlConnectionPoolService : IHostedService, IAsyncDisposable
{
    private readonly SqlPoolingOptions _poolingOptions;
    private readonly ISqlProviderFactory _providerFactory;
    private readonly ConcurrentDictionary<string, string> _connectionStringCache = new();
    private readonly ConcurrentDictionary<string, DbConnection> _warmupConnections = new();
    private Timer? _maintenanceTimer;
    private readonly SemaphoreSlim _maintenanceGate = new(1, 1);
    private readonly TimeSpan _maintenanceInterval = TimeSpan.FromMinutes(5);
    private readonly CancellationTokenSource _cts = new();
    private volatile bool _disposed;

    public SqlConnectionPoolService(SqlPoolingOptions poolingOptions, ISqlProviderFactory providerFactory)
    {
        _poolingOptions = poolingOptions;
        _providerFactory = providerFactory;

        // Start from clean driver pools regardless of which providers this deployment uses
        SqlConnection.ClearAllPools();
        Npgsql.NpgsqlConnection.ClearAllPools();
        MySqlConnector.MySqlConnection.ClearAllPools();

        Log.Information("Database Connection Pool initialized with Min: {MinPoolSize}, Max: {MaxPoolSize}, Timeout: {Timeout}s, AppName: '{AppName}'",
            _poolingOptions.MinPoolSize, _poolingOptions.MaxPoolSize, _poolingOptions.ConnectionTimeout, _poolingOptions.ApplicationName);
    }

    /// <summary>Returns an optimized connection string for the detected provider</summary>
    public string OptimizeConnectionString(string connectionString)
    {
        if (_connectionStringCache.TryGetValue(connectionString, out var optimized))
            return optimized;

        var provider = _providerFactory.GetProvider(connectionString);
        var result = provider.OptimizeConnectionString(connectionString, _poolingOptions);
        _connectionStringCache[connectionString] = result;
        return result;
    }

    /// <summary>Creates an open-ready connection for the detected provider. Returns DbConnection to support all ADO.NET providers</summary>
    public DbConnection CreateConnection(string connectionString)
    {
        var optimized = OptimizeConnectionString(connectionString);
        var provider = _providerFactory.GetProvider(connectionString);
        return provider.CreateConnection(optimized);
    }

    /// <summary>Prewarms the connection pool for a given connection string</summary>
    public async Task PrewarmConnectionPoolAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        if (!_poolingOptions.EnablePooling)
            return;

        var optimized = OptimizeConnectionString(connectionString);
        var provider = _providerFactory.GetProvider(connectionString);

        try
        {
            Log.Information("Prewarming connection pool ({Provider}) for connection string...", provider.ProviderType);

            var connections = new List<DbConnection>();

            for (int i = 0; i < _poolingOptions.MinPoolSize; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var conn = provider.CreateConnection(optimized);
                await conn.OpenAsync(cancellationToken);
                connections.Add(conn);

                if (i == 0)
                {
                    _warmupConnections[optimized] = conn;
                    connections.RemoveAt(0);
                }
            }

            foreach (var conn in connections)
            {
                await conn.CloseAsync();
                await conn.DisposeAsync();
            }

            Log.Information("Connection pool prewarmed with {Count} connections", _poolingOptions.MinPoolSize);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error prewarming connection pool");
        }
    }

    internal async Task MaintenanceTaskAsync()
    {
        // Timer does not await the callback, so a stalled pass would otherwise overlap the next tick
        if (!await _maintenanceGate.WaitAsync(0))
            return;

        try
        {
            foreach (var entry in _warmupConnections)
            {
                if (_cts.IsCancellationRequested)
                    break;

                string connStr = entry.Key;
                DbConnection connection = entry.Value;

                try
                {
                    if (connection.State != ConnectionState.Open)
                        await connection.OpenAsync();

                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT 1";
                    cmd.CommandTimeout = 5;
                    await cmd.ExecuteScalarAsync(_cts.Token);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error with maintenance connection, recreating...");

                    try { await connection.DisposeAsync(); }
                    catch (Exception disposeEx) { Log.Debug(disposeEx, "Error disposing connection during maintenance"); }

                    var provider = _providerFactory.GetProvider(connStr);
                    var newConn = provider.CreateConnection(connStr);
                    await newConn.OpenAsync();
                    _warmupConnections[connStr] = newConn;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error performing connection pool maintenance");
        }
        finally
        {
            _maintenanceGate.Release();
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _maintenanceTimer = new Timer(
            _ => _ = MaintenanceTaskAsync(),
            null,
            TimeSpan.FromSeconds(30),
            _maintenanceInterval);

        Log.Debug("SQL Connection Pool Service started with maintenance interval: {Interval} minutes",
            _maintenanceInterval.TotalMinutes);

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // The host stops hosted services and the container disposes singletons, so this runs twice
        if (_disposed)
            return;

        _cts.Cancel();
        _maintenanceTimer?.Change(Timeout.Infinite, 0);

        // Drain an in-flight maintenance pass before disposing the connections it is using
        if (await _maintenanceGate.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None))
            _maintenanceGate.Release();

        foreach (var connection in _warmupConnections.Values)
        {
            try
            {
                await connection.CloseAsync();
                await connection.DisposeAsync();
            }
            catch (Exception ex) when (ex is DbException or InvalidOperationException)
            {
                Log.Warning(ex, "Failed to close a warmup connection during shutdown");
            }
        }

        _warmupConnections.Clear();

        Log.Information("SQL Connection Pool Service stopped");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        await StopAsync(CancellationToken.None);
        _disposed = true;
        _cts.Dispose();
        _maintenanceTimer?.Dispose();
        _maintenanceGate.Dispose();
    }
}

public static class SqlConnectionPoolingExtensions
{
    public static IServiceCollection AddSqlConnectionPooling(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new SqlPoolingOptions(
            MinPoolSize: configuration.GetValue<int>("SqlConnectionPooling:MinPoolSize", 5),
            MaxPoolSize: configuration.GetValue<int>("SqlConnectionPooling:MaxPoolSize", 100),
            ConnectionTimeout: configuration.GetValue<int>("SqlConnectionPooling:ConnectionTimeout", 15),
            EnablePooling: configuration.GetValue<bool>("SqlConnectionPooling:Enabled", true),
            ApplicationName: configuration.GetValue<string>("SqlConnectionPooling:ApplicationName", "PortwayAPI")!
        );

        services.AddSingleton(options);

        services.AddSingleton<SqlConnectionPoolService>(sp => new SqlConnectionPoolService(
            options,
            sp.GetRequiredService<ISqlProviderFactory>()
        ));

        services.AddHostedService(sp => sp.GetRequiredService<SqlConnectionPoolService>());

        return services;
    }
}
