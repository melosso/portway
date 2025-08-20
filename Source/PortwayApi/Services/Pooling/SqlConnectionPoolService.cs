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
using Serilog;

namespace PortwayApi.Services;

/// <summary>
/// Configures and manages SQL connection pooling for improved performance
/// </summary>
public class SqlConnectionPoolService : IHostedService
{
    private readonly int _minPoolSize;
    private readonly int _maxPoolSize;
    private readonly int _connectionTimeout;
    private readonly int _commandTimeout;
    private readonly bool _enablePooling;
    private readonly string _applicationName;
    private readonly ConcurrentDictionary<string, string> _connectionStringCache = new();
    private readonly ConcurrentDictionary<string, SqlConnection> _warmupConnections = new();
    private Timer? _maintenanceTimer;
    private readonly TimeSpan _maintenanceInterval = TimeSpan.FromMinutes(5);
    
    /// <summary>
    /// Creates a new SQL connection pool service with the specified parameters
    /// </summary>
    /// <param name="minPoolSize">Minimum number of connections to maintain in the pool</param>
    /// <param name="maxPoolSize">Maximum number of connections allowed in the pool</param>
    /// <param name="connectionTimeout">Timeout in seconds for establishing connections</param>
    /// <param name="commandTimeout">Default command timeout in seconds</param>
    /// <param name="enablePooling">Whether connection pooling is enabled</param>
    /// <param name="applicationName">The application name to use for SQL connections</param>
    public SqlConnectionPoolService(
        int minPoolSize = 5,
        int maxPoolSize = 100,
        int connectionTimeout = 15,
        int commandTimeout = 30,
        bool enablePooling = true,
        string applicationName = "PortwayAPI"
    )
    {
        _minPoolSize = minPoolSize;
        _maxPoolSize = maxPoolSize;
        _connectionTimeout = connectionTimeout;
        _commandTimeout = commandTimeout;
        _enablePooling = enablePooling;
        _applicationName = applicationName;
        
        // Configure default connection pooling parameters
        SqlConnection.ClearAllPools();
        
        Log.Information("🔌 SQL Connection Pool Service initialized with Min: {MinPoolSize}, Max: {MaxPoolSize}, Timeout: {Timeout}s, AppName: {AppName}",
            _minPoolSize, _maxPoolSize, _connectionTimeout, _applicationName);
    }
    
    /// <summary>
    /// Optimize a connection string for connection pooling
    /// </summary>
    public string OptimizeConnectionString(string connectionString)
    {
        // Check if already cached
        if (_connectionStringCache.TryGetValue(connectionString, out var optimized))
        {
            return optimized;
        }
        
        // Parse the connection string
        var builder = new SqlConnectionStringBuilder(connectionString);
        
        // Apply connection pooling settings
        builder.MinPoolSize = _minPoolSize;
        builder.MaxPoolSize = _maxPoolSize;
        builder.ConnectTimeout = _connectionTimeout;
        builder.Pooling = _enablePooling;
        
        // Set the application name to identify your connections
        builder.ApplicationName = _applicationName;
        
        // Cache the optimized connection string
        var result = builder.ConnectionString;
        _connectionStringCache[connectionString] = result;
        
        return result;
    }
    
    /// <summary>
    /// Creates a connection with the optimized connection string and configured command timeout
    /// </summary>
    public SqlConnection CreateConnection(string connectionString)
    {
        var optimizedConnectionString = OptimizeConnectionString(connectionString);
        var connection = new SqlConnection(optimizedConnectionString);
        
        return connection;
    }
    
    /// <summary>
    /// Prewarms the connection pool for a given environment
    /// </summary>
    public async Task PrewarmConnectionPoolAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        if (!_enablePooling)
            return;
            
        var optimizedConnectionString = OptimizeConnectionString(connectionString);
        
        try
        {
            Log.Information("🔄 Prewarming connection pool for connection string...");
            
            // Create and open multiple connections to fill the minimum pool size
            var connections = new List<SqlConnection>();
            
            for (int i = 0; i < _minPoolSize; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;
                    
                var connection = new SqlConnection(optimizedConnectionString);
                await connection.OpenAsync(cancellationToken);
                connections.Add(connection);
                
                // Keep one connection for periodic keep-alive
                if (i == 0)
                {
                    _warmupConnections[optimizedConnectionString] = connection;
                    connections.RemoveAt(0);
                }
            }
            
            // Close the extra connections, they will return to the pool
            foreach (var conn in connections)
            {
                await conn.CloseAsync();
                await conn.DisposeAsync();
            }
            
            Log.Information("✅ Connection pool prewarmed with {Count} connections", _minPoolSize);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ Error prewarming connection pool");
        }
    }
    
    /// <summary>
    /// Maintenance task to keep connections alive and monitor pool health
    /// </summary>
    private async Task MaintenanceTaskAsync(object? state)
    {
        try
        {
            foreach (var connectionEntry in _warmupConnections)
            {
                string connectionString = connectionEntry.Key;
                SqlConnection connection = connectionEntry.Value;
                
                try
                {
                    // Check if connection is still open
                    if (connection.State != ConnectionState.Open)
                    {
                        // Try to reopen
                        await connection.OpenAsync();
                        Log.Debug("🔄 Reopened maintenance connection for pool");
                    }
                    
                    // Execute a simple query to keep the connection alive
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT 1";
                    cmd.CommandTimeout = 5; // Short timeout for heartbeat
                    await cmd.ExecuteScalarAsync();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "⚠️ Error with maintenance connection, recreating...");
                    
                    // Dispose old connection
                    try { await connection.DisposeAsync(); } catch { }
                    
                    // Create a new connection
                    var newConnection = new SqlConnection(connectionString);
                    await newConnection.OpenAsync();
                    _warmupConnections[connectionString] = newConnection;
                }
            }
            
            // Get pool statistics every 10 minutes (occasional diagnostics)
            if (DateTime.UtcNow.Minute % 10 == 0 && DateTime.UtcNow.Second < 10)
            {
                Log.Debug("📊 SQL Connection Pool Status: {Status}", 
                    "Connection pool statistics logging is not implemented.");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ Error performing connection pool maintenance");
        }
    }

    // IHostedService implementation
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _maintenanceTimer = new Timer(
            async state => await MaintenanceTaskAsync(state),
            null,
            TimeSpan.FromSeconds(30), // Start after 30 seconds
            _maintenanceInterval);
            
        Log.Debug("✅ SQL Connection Pool Service started with maintenance interval: {Interval} minutes", 
            _maintenanceInterval.TotalMinutes);
            
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _maintenanceTimer?.Change(Timeout.Infinite, 0);
        
        // Close and dispose all warmup connections
        foreach (var connection in _warmupConnections.Values)
        {
            try 
            { 
                connection.Close();
                connection.Dispose();
            }
            catch { }
        }
        
        _warmupConnections.Clear();
        
        Log.Information("🛑 SQL Connection Pool Service stopped");
        return Task.CompletedTask;
    }
}

public static class SqlConnectionPoolingExtensions
{
    /// <summary>
    /// Adds SQL connection pooling services to the service collection
    /// </summary>
    public static IServiceCollection AddSqlConnectionPooling(this IServiceCollection services, IConfiguration configuration)
    {
        // Get configuration values with defaults
        int minPoolSize = configuration.GetValue<int>("SqlConnectionPooling:MinPoolSize", 5);
        int maxPoolSize = configuration.GetValue<int>("SqlConnectionPooling:MaxPoolSize", 100);
        int connectionTimeout = configuration.GetValue<int>("SqlConnectionPooling:ConnectionTimeout", 15);
        int commandTimeout = configuration.GetValue<int>("SqlConnectionPooling:CommandTimeout", 30);
        bool enablePooling = configuration.GetValue<bool>("SqlConnectionPooling:Enabled", true);
        string applicationName = configuration.GetValue<string>("SqlConnectionPooling:ApplicationName", "PortwayAPI");
        
        // Register the service
        services.AddSingleton<SqlConnectionPoolService>(sp => new SqlConnectionPoolService(
            minPoolSize,
            maxPoolSize,
            connectionTimeout,
            commandTimeout,
            enablePooling,
            applicationName
        ));
        
        // Register as a hosted service for maintenance
        services.AddHostedService(sp => sp.GetRequiredService<SqlConnectionPoolService>());
        
        return services;
    }
}