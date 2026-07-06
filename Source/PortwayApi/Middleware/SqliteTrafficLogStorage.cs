namespace PortwayApi.Middleware;

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PortwayApi.Auth;
using Serilog;

/// <summary>SQLite implementation of log storage</summary>
public class SqliteTrafficLogStorage : ITrafficLogStorage
{
    private readonly ProxyTrafficLoggerOptions _options;
    private readonly string _connectionString;

    public SqliteTrafficLogStorage(IOptions<ProxyTrafficLoggerOptions> options)
    {
        _options = options.Value;
        _connectionString = $"Data Source={_options.SqlitePath}";
    }

    public async Task InitializeAsync()
    {
        try
        {
            // Ensure directory exists
            var directory = Path.GetDirectoryName(_options.SqlitePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                Log.Information("Created SQLite log directory: {Directory}", directory);
            }

            // Create database and tables if they don't exist
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            
            // Create traffic logs table
            var createTableCommand = connection.CreateCommand();
            createTableCommand.CommandText = @"
                CREATE TABLE IF NOT EXISTS TrafficLogs (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Timestamp TEXT NOT NULL,
                    Method TEXT NOT NULL,
                    Path TEXT NOT NULL,
                    QueryString TEXT,
                    Environment TEXT,
                    EndpointName TEXT,
                    TargetUrl TEXT,
                    StatusCode INTEGER,
                    RequestSize INTEGER,
                    ResponseSize INTEGER,
                    DurationMs INTEGER,
                    Username TEXT,
                    ClientIp TEXT,
                    TraceId TEXT NOT NULL,
                    RequestHeaders TEXT,
                    RequestBody TEXT,
                    ResponseBody TEXT
                )";
                
            await createTableCommand.ExecuteNonQueryAsync();
            
            // Create index on Timestamp
            var createIndexCommand = connection.CreateCommand();
            createIndexCommand.CommandText = @"
                CREATE INDEX IF NOT EXISTS idx_timestamp ON TrafficLogs (Timestamp)";
                
            await createIndexCommand.ExecuteNonQueryAsync();
            
            Log.Information("Traffic tracing database initialized: {DbPath}", _options.SqlitePath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error initializing SQLite storage for traffic logs");
            throw;
        }
    }

    public async Task SaveLogsAsync(IEnumerable<ProxyTrafficLogEntry> logs)
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // Create a transaction for better performance
            await using var transaction = connection.BeginTransaction();
            
            try
            {
                // Create a reusable command
                await using var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO TrafficLogs (
                        Timestamp, Method, Path, QueryString, Environment, EndpointName, 
                        TargetUrl, StatusCode, RequestSize, ResponseSize, DurationMs, 
                        Username, ClientIp, TraceId, RequestHeaders, RequestBody, ResponseBody
                    ) VALUES (
                        @Timestamp, @Method, @Path, @QueryString, @Environment, @EndpointName,
                        @TargetUrl, @StatusCode, @RequestSize, @ResponseSize, @DurationMs,
                        @Username, @ClientIp, @TraceId, @RequestHeaders, @RequestBody, @ResponseBody
                    )";
                
                // Create parameters
                var timestampParam = command.CreateParameter();
                timestampParam.ParameterName = "@Timestamp";
                command.Parameters.Add(timestampParam);
                
                var methodParam = command.CreateParameter();
                methodParam.ParameterName = "@Method";
                command.Parameters.Add(methodParam);
                
                var pathParam = command.CreateParameter();
                pathParam.ParameterName = "@Path";
                command.Parameters.Add(pathParam);
                
                var queryStringParam = command.CreateParameter();
                queryStringParam.ParameterName = "@QueryString";
                command.Parameters.Add(queryStringParam);
                
                var environmentParam = command.CreateParameter();
                environmentParam.ParameterName = "@Environment";
                command.Parameters.Add(environmentParam);
                
                var endpointNameParam = command.CreateParameter();
                endpointNameParam.ParameterName = "@EndpointName";
                command.Parameters.Add(endpointNameParam);
                
                var targetUrlParam = command.CreateParameter();
                targetUrlParam.ParameterName = "@TargetUrl";
                command.Parameters.Add(targetUrlParam);
                
                var statusCodeParam = command.CreateParameter();
                statusCodeParam.ParameterName = "@StatusCode";
                command.Parameters.Add(statusCodeParam);
                
                var requestSizeParam = command.CreateParameter();
                requestSizeParam.ParameterName = "@RequestSize";
                command.Parameters.Add(requestSizeParam);
                
                var responseSizeParam = command.CreateParameter();
                responseSizeParam.ParameterName = "@ResponseSize";
                command.Parameters.Add(responseSizeParam);
                
                var durationMsParam = command.CreateParameter();
                durationMsParam.ParameterName = "@DurationMs";
                command.Parameters.Add(durationMsParam);
                
                var usernameParam = command.CreateParameter();
                usernameParam.ParameterName = "@Username";
                command.Parameters.Add(usernameParam);
                
                var clientIpParam = command.CreateParameter();
                clientIpParam.ParameterName = "@ClientIp";
                command.Parameters.Add(clientIpParam);
                
                var traceIdParam = command.CreateParameter();
                traceIdParam.ParameterName = "@TraceId";
                command.Parameters.Add(traceIdParam);
                
                var requestHeadersParam = command.CreateParameter();
                requestHeadersParam.ParameterName = "@RequestHeaders";
                command.Parameters.Add(requestHeadersParam);
                
                var requestBodyParam = command.CreateParameter();
                requestBodyParam.ParameterName = "@RequestBody";
                command.Parameters.Add(requestBodyParam);
                
                var responseBodyParam = command.CreateParameter();
                responseBodyParam.ParameterName = "@ResponseBody";
                command.Parameters.Add(responseBodyParam);
                
                // Insert each log
                foreach (var log in logs)
                {
                    // Set parameter values
                    timestampParam.Value = log.Timestamp.ToString("o");
                    methodParam.Value = log.Method;
                    pathParam.Value = log.Path;
                    queryStringParam.Value = log.QueryString ?? (object)DBNull.Value;
                    environmentParam.Value = log.Environment ?? (object)DBNull.Value;
                    endpointNameParam.Value = log.EndpointName ?? (object)DBNull.Value;
                    targetUrlParam.Value = log.TargetUrl ?? (object)DBNull.Value;
                    statusCodeParam.Value = log.StatusCode;
                    requestSizeParam.Value = log.RequestSize;
                    responseSizeParam.Value = log.ResponseSize;
                    durationMsParam.Value = log.DurationMs;
                    usernameParam.Value = log.Username ?? (object)DBNull.Value;
                    clientIpParam.Value = log.ClientIp;
                    traceIdParam.Value = log.TraceId;
                    
                    // Convert dictionary to JSON
                    string? requestHeadersJson = log.RequestHeaders?.Count > 0 
                        ? JsonSerializer.Serialize(log.RequestHeaders) 
                        : null;
                    requestHeadersParam.Value = requestHeadersJson ?? (object)DBNull.Value;
                    
                    // Request and response bodies
                    requestBodyParam.Value = log.RequestBody ?? (object)DBNull.Value;
                    responseBodyParam.Value = log.ResponseBody ?? (object)DBNull.Value;
                    
                    // Execute insert
                    await command.ExecuteNonQueryAsync();
                }
                
                // Commit the transaction
                await transaction.CommitAsync();
                Log.Debug("Successfully saved {Count} traffic logs to SQLite", logs.Count());
            }
            catch (Exception ex)
            {
                // Rollback on error
                await transaction.RollbackAsync();
                throw new Exception("Error saving traffic logs to SQLite", ex);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error saving traffic logs to SQLite");
            throw;
        }
    }
}
