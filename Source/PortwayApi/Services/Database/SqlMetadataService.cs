using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using PortwayApi.Classes;
using PortwayApi.Services.Providers;
using Serilog;

namespace PortwayApi.Services;

public class SqlMetadataService
{
    private readonly ConcurrentDictionary<string, IReadOnlyList<Services.Database.ColumnMetadata>> _objectMetadataCache = new();
    private readonly ConcurrentDictionary<string, IReadOnlyList<Services.Database.ParameterMetadata>> _procedureMetadataCache = new();
    private readonly SqlConnectionPoolService _connectionPoolService;
    private readonly ISqlProviderFactory _providerFactory;
    private int _initialized;
    private EnvironmentSettings? _environmentSettings;
    private Func<string, Task<string>>? _getConnectionStringAsync;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private int _refreshQueued;
    private readonly ConcurrentDictionary<string, string> _sourceSignatures = new(StringComparer.OrdinalIgnoreCase);

    public SqlMetadataService(SqlConnectionPoolService connectionPoolService, ISqlProviderFactory providerFactory)
    {
        _connectionPoolService = connectionPoolService;
        _providerFactory = providerFactory;
    }

    public virtual async Task InitializeAsync(
        IReadOnlyDictionary<string, Classes.EndpointDefinition> sqlEndpoints,
        EnvironmentSettings environmentSettings,
        Func<string, Task<string>> getConnectionStringAsync,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _initialized, 1, 0) != 0)
        {
            Log.Warning("SqlMetadataService is already initialized. Skipping re-initialization.");
            return;
        }

        Log.Debug("Initializing SQL metadata cache for {Count} endpoints...", sqlEndpoints.Count);
        _environmentSettings = environmentSettings;
        _getConnectionStringAsync = getConnectionStringAsync;

        try
        {
            LogFailures(await Task.WhenAll(BuildInitTasks(sqlEndpoints, environmentSettings, getConnectionStringAsync, cancellationToken)));
        }
        catch (Exception ex)
        {
            Log.Error("One or more metadata initialization tasks failed: {ErrorType} - {ErrorMessage}",
                ex.GetType().Name, ex.Message);
        }

        Log.Debug("SQL metadata cache initialized successfully. Objects: {ObjectCount}, Procedures: {ProcedureCount}",
            _objectMetadataCache.Count, _procedureMetadataCache.Count);
    }

    /// <summary>
    /// Runs RefreshAsync in the background so an unreachable database never delays hot reload; at most one refresh runs and one waits
    /// </summary>
    public virtual void QueueRefresh()
    {
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 1)
            return;

        _ = Task.Run(async () =>
        {
            await _refreshGate.WaitAsync();
            try
            {
                // cleared before the scan so a change during the scan queues the next run
                Interlocked.Exchange(ref _refreshQueued, 0);
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "SQL metadata refresh failed");
            }
            finally
            {
                _refreshGate.Release();
            }
        });
    }

    /// <summary>
    /// Reloads column metadata for the current SQL endpoint files; no-op before startup initialization ran
    /// </summary>
    public virtual async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_environmentSettings is null || _getConnectionStringAsync is null)
            return;

        var sqlEndpoints = EndpointHandler.GetSqlEndpoints();
        var changed = sqlEndpoints
                .Where(kv => !_sourceSignatures.TryGetValue(kv.Key, out var known) || known != Signature(kv.Value))
                .ToDictionary(StringComparer.OrdinalIgnoreCase);
        LogFailures(await Task.WhenAll(BuildInitTasks(changed, _environmentSettings, _getConnectionStringAsync, cancellationToken)));

        foreach (var removed in _objectMetadataCache.Keys.Where(k => !sqlEndpoints.ContainsKey(k)))
            _objectMetadataCache.TryRemove(removed, out _);
        foreach (var removed in _procedureMetadataCache.Keys.Where(k => !sqlEndpoints.ContainsKey(k)))
            _procedureMetadataCache.TryRemove(removed, out _);
        foreach (var removed in _sourceSignatures.Keys.Where(k => !sqlEndpoints.ContainsKey(k)))
            _sourceSignatures.TryRemove(removed, out _);
    }

    // settings that metadata loading reads; a refresh skips endpoints with an unchanged signature
    private static string Signature(Classes.EndpointDefinition d) => string.Join('|',
        d.DatabaseSchema, d.DatabaseObjectName, d.DatabaseObjectType, d.Procedure,
        string.Join(',', d.AllowedColumns ?? []), string.Join(',', d.AllowedEnvironments ?? []));

    // one line per load, not per endpoint, so an unreachable database does not flood the log
    private static void LogFailures(MetadataLoadResult[] results)
    {
        var failed = results.Where(r => r.Error is not null).ToList();
        if (failed.Count == 0)
            return;

        Log.Warning("SQL metadata not loaded for {Count} endpoint(s): {Endpoints}. Environments tried: {Environments}. First error: {Error}",
            failed.Count,
            string.Join(", ", failed.Select(f => f.Endpoint)),
            string.Join(", ", failed.SelectMany(f => f.Environments).Distinct(StringComparer.OrdinalIgnoreCase)),
            failed[0].Error);
    }

    private sealed record MetadataLoadResult(string Endpoint, IReadOnlyList<string> Environments, string? Error);

    private List<Task<MetadataLoadResult>> BuildInitTasks(
        IReadOnlyDictionary<string, Classes.EndpointDefinition> sqlEndpoints,
        EnvironmentSettings environmentSettings,
        Func<string, Task<string>> getConnectionStringAsync,
        CancellationToken cancellationToken)
    {
        var initTasks = new List<Task<MetadataLoadResult>>();
        foreach (var (key, definition) in sqlEndpoints)
            _sourceSignatures[key] = Signature(definition);
        var globalEnvironments = environmentSettings.GetAllowedEnvironments().Take(3).ToList();

        foreach (var endpoint in sqlEndpoints)
        {
            var endpointName = endpoint.Key;
            var definition = endpoint.Value;

            if (string.IsNullOrEmpty(definition.DatabaseObjectName))
                continue;

            var environmentsToTry = new List<string>();
            if (definition.AllowedEnvironments != null && definition.AllowedEnvironments.Any())
                environmentsToTry.AddRange(definition.AllowedEnvironments);

            foreach (var env in globalEnvironments)
            {
                if (!environmentsToTry.Contains(env, StringComparer.OrdinalIgnoreCase))
                    environmentsToTry.Add(env);
            }

            if (!environmentsToTry.Any())
            {
                Log.Warning("Endpoint {EndpointName} has no allowed or global environments, skipping metadata initialization", endpointName);
                continue;
            }

            initTasks.Add(InitializeEndpointMetadataAsync(
                endpointName,
                definition,
                environmentsToTry,
                getConnectionStringAsync,
                cancellationToken));
        }

        return initTasks;
    }

    private async Task<MetadataLoadResult> InitializeEndpointMetadataAsync(
        string endpointName,
        Classes.EndpointDefinition definition,
        List<string> environments,
        Func<string, Task<string>> getConnectionStringAsync,
        CancellationToken cancellationToken = default)
    {
        string? lastError = null;
        foreach (var environment in environments)
        {
            try
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

                var connectionString = await getConnectionStringAsync(environment);
                if (string.IsNullOrEmpty(connectionString))
                {
                    lastError ??= $"no connection string for environment {environment}";
                    Log.Debug("No connection string found for environment {Environment}, trying next environment for {EndpointName}",
                        environment, endpointName);
                    continue;
                }

                int columnCount = await LoadObjectMetadataForEndpointAsync(endpointName, definition, connectionString, cts.Token);

                if (columnCount == 0)
                {
                    lastError ??= $"{definition.DatabaseObjectName} has no columns in environment {environment}";
                    Log.Debug("No columns found for {EndpointName} in environment {Environment}, trying next environment",
                        endpointName, environment);
                    continue;
                }

                if (HasModificationMethods(definition) && !string.IsNullOrEmpty(definition.Procedure))
                    await LoadProcedureMetadataForEndpointAsync(endpointName, definition, connectionString, cts.Token);

                Log.Debug("Successfully initialized metadata for {EndpointName} using environment {Environment} ({ColumnCount} columns)",
                    endpointName, environment, columnCount);
                return new(endpointName, environments, null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new(endpointName, environments, null);
            }
            catch (OperationCanceledException)
            {
                lastError = $"timed out after 30 seconds in environment {environment}";
                Log.Debug("Metadata initialization timed out for endpoint {EndpointName} in environment {Environment} after 30 seconds",
                    endpointName, environment);
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                Log.Debug("Failed to initialize metadata for endpoint {EndpointName} in environment {Environment}: {ErrorMessage}. Trying next environment...",
                    endpointName, environment, ex.Message);
            }
        }

        return new(endpointName, environments, lastError ?? "no environment returned metadata");
    }

    private bool HasModificationMethods(Classes.EndpointDefinition definition)
        => !string.IsNullOrEmpty(definition.Procedure);

    private async Task<int> LoadObjectMetadataForEndpointAsync(
        string endpointName,
        Classes.EndpointDefinition definition,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var objectName = definition.DatabaseObjectName!;
        var objectType = definition.DatabaseObjectType ?? "Table";

        var provider = _providerFactory.GetProvider(connectionString);
        var optimizedConnectionString = _connectionPoolService.OptimizeConnectionString(connectionString);
        await using var connection = provider.CreateConnection(optimizedConnectionString);
        await connection.OpenAsync(cancellationToken);

        var schema = Helpers.SqlSchemaResolver.Resolve(definition.DatabaseSchema, provider, connection.Database);

        Log.Debug("Loading object metadata for {EndpointName}: {Schema}.{ObjectName} ({ObjectType})",
            endpointName, schema, objectName, objectType);

        List<Services.Database.ColumnMetadata> columns;

        switch (objectType.ToLowerInvariant())
        {
            case "table":
            case "view":
                columns = await GetTableColumnsAsync(connection, provider, schema, objectName, cancellationToken).ConfigureAwait(false);
                break;

            case "tablevaluedfunction":
                if (!provider.SupportsTvf)
                {
                    // Configuration error: this endpoint can never serve requests on this provider
                    Log.Error("Endpoint {EndpointName} is configured as a table valued function, but provider {Provider} does not support TVFs. Requests to this endpoint will fail; change the endpoint type or the environment's database",
                        endpointName, provider.ProviderType);
                    return 0;
                }
                columns = await provider.GetTvfColumnsAsync(connection, schema, objectName, cancellationToken).ConfigureAwait(false);
                break;

            default:
                Log.Warning("Unknown database object type '{ObjectType}' for endpoint {EndpointName}", objectType, endpointName);
                return 0;
        }

        columns = ProcessColumnMetadata(columns, definition, endpointName);

        _objectMetadataCache[endpointName] = columns;

        Log.Debug("Cached object metadata for {EndpointName}: {ColumnCount} columns", endpointName, columns.Count);
        return columns.Count;
    }

    private async Task LoadProcedureMetadataForEndpointAsync(
        string endpointName,
        Classes.EndpointDefinition definition,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var procedureName = definition.Procedure!;
        var provider = _providerFactory.GetProvider(connectionString);

        if (!provider.SupportsProcedures)
        {
            Log.Information("Provider {Provider} does not support stored procedures; skipping procedure metadata for {EndpointName}", provider.ProviderType, endpointName);
            return;
        }

        Log.Debug("Loading procedure metadata for {EndpointName}: {ProcedureName}", endpointName, procedureName);

        var optimizedConnectionString = _connectionPoolService.OptimizeConnectionString(connectionString);
        await using var connection = provider.CreateConnection(optimizedConnectionString);
        await connection.OpenAsync(cancellationToken);

        var parts = procedureName.Split('.');
        string schema, name;
        if (parts.Length == 2)
        {
            schema = Helpers.SqlSchemaResolver.Resolve(parts[0], provider, connection.Database);
            name = parts[1];
        }
        else
        {
            schema = Helpers.SqlSchemaResolver.Resolve(null, provider, connection.Database);
            name = procedureName;
        }

        var parameters = await provider.GetProcedureParametersAsync(connection, schema, name, cancellationToken).ConfigureAwait(false);
        parameters = parameters.Where(p => !IsReservedParameter(p.ParameterName)).ToList();

        _procedureMetadataCache[endpointName] = parameters;

        Log.Debug("Cached procedure metadata for {EndpointName}: {ParameterCount} parameters", endpointName, parameters.Count);
    }

    private bool IsReservedParameter(string parameterName)
    {
        var reservedParameters = new[] { "@method", "@action", "@operation" };
        return reservedParameters.Contains(parameterName.ToLowerInvariant());
    }

    private List<Services.Database.ColumnMetadata> ProcessColumnMetadata(List<Services.Database.ColumnMetadata> columns, Classes.EndpointDefinition definition, string endpointName)
    {
        var primaryKey = definition.PrimaryKey;
        var allowedColumns = definition.AllowedColumns ?? new List<string>();

        if (!string.IsNullOrEmpty(primaryKey))
        {
            var pkColumn = columns.FirstOrDefault(c =>
                c.DatabaseColumnName.Equals(primaryKey, StringComparison.OrdinalIgnoreCase));
            if (pkColumn != null)
                pkColumn.IsPrimaryKey = true;
        }

        if (allowedColumns.Any())
        {
            var (aliasToDatabase, databaseToAlias) =
                Helpers.ColumnMappingHelper.ParseColumnMappings(allowedColumns);

            var allowedDatabaseColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var dbColumn in aliasToDatabase.Values)
                allowedDatabaseColumns.Add(dbColumn);

            foreach (var columnDef in allowedColumns)
            {
                if (!columnDef.Contains(" as ", StringComparison.OrdinalIgnoreCase))
                {
                    var simpleColumn = columnDef.Trim();
                    allowedDatabaseColumns.Add(simpleColumn);

                    if (!databaseToAlias.ContainsKey(simpleColumn))
                        databaseToAlias[simpleColumn] = simpleColumn;
                }
            }

            var filteredColumns = columns.Where(c => allowedDatabaseColumns.Contains(c.DatabaseColumnName)).ToList();

            foreach (var column in filteredColumns)
            {
                string finalColumnName = column.DatabaseColumnName;

                if (databaseToAlias.TryGetValue(column.DatabaseColumnName, out var alias))
                    finalColumnName = alias;

                if (string.IsNullOrWhiteSpace(finalColumnName))
                {
                    Log.Warning("Empty column name detected for database column {DatabaseColumn} in endpoint {EndpointName}",
                        column.DatabaseColumnName, endpointName);
                    finalColumnName = "UnknownColumn";
                }

                column.ColumnName = finalColumnName;
            }

            Log.Debug("Filtered to {Count} allowed columns for {EndpointName}", filteredColumns.Count, endpointName);
            foreach (var column in filteredColumns)
                Log.Debug("Final column mapping: {DatabaseName} -> {ColumnName}", column.DatabaseColumnName, column.ColumnName);

            return filteredColumns;
        }
        else
        {
            foreach (var column in columns)
                column.ColumnName = column.DatabaseColumnName;

            return columns;
        }
    }

    /// <summary>
    /// Gets table/view columns using ADO.NET GetSchemaAsync first; falls back to provider-specific PRAGMA for SQLite
    /// </summary>
    private async Task<List<Services.Database.ColumnMetadata>> GetTableColumnsAsync(
        DbConnection connection,
        ISqlProvider provider,
        string schema,
        string tableName,
        CancellationToken cancellationToken)
    {
        // SQLite uses PRAGMA, GetSchemaAsync("Columns") is not reliably supported
        if (provider is SqliteProvider sqliteProvider)
            return await sqliteProvider.GetColumnsViaPragmaAsync(connection, tableName, cancellationToken).ConfigureAwait(false);

        var columns = new List<Services.Database.ColumnMetadata>();

        var restrictions = new[] { null, schema, tableName, (string?)null };
        var columnsTable = await connection.GetSchemaAsync("Columns", restrictions, cancellationToken);

        Log.Debug("Found {Count} columns for {Schema}.{TableName}:", columnsTable.Rows.Count, schema, tableName);

        foreach (DataRow row in columnsTable.Rows)
        {
            var columnName = row["COLUMN_NAME"].ToString() ?? string.Empty;
            Log.Debug("  - Column: '{ColumnName}', DataType: {DataType}, Nullable: {IsNullable}",
                columnName,
                row["DATA_TYPE"].ToString() ?? "unknown",
                row["IS_NULLABLE"].ToString() ?? "unknown");

            var col = new Services.Database.ColumnMetadata
            {
                DatabaseColumnName = columnName,
                DataType = row["DATA_TYPE"].ToString() ?? string.Empty,
                IsNullable = row["IS_NULLABLE"].ToString()?.Equals("YES", StringComparison.OrdinalIgnoreCase) ?? false,
                MaxLength = row["CHARACTER_MAXIMUM_LENGTH"] != DBNull.Value
                    ? Convert.ToInt32(row["CHARACTER_MAXIMUM_LENGTH"]) : null,
                NumericPrecision = row["NUMERIC_PRECISION"] != DBNull.Value
                    ? Convert.ToInt32(row["NUMERIC_PRECISION"]) : null,
                NumericScale = row["NUMERIC_SCALE"] != DBNull.Value
                    ? Convert.ToInt32(row["NUMERIC_SCALE"]) : null
            };

            col.ClrType = provider.MapSqlTypeToClr(col.DataType);
            columns.Add(col);
        }

        return columns;
    }

    // Public metadata retrieval methods

    public IReadOnlyList<Services.Database.ColumnMetadata>? GetObjectMetadata(string endpointName) =>
        _objectMetadataCache.GetValueOrDefault(endpointName);

    public IReadOnlyList<Services.Database.ParameterMetadata>? GetProcedureMetadata(string endpointName) =>
        _procedureMetadataCache.GetValueOrDefault(endpointName);

    public bool HasObjectMetadata(string endpointName) => _objectMetadataCache.ContainsKey(endpointName);

    public bool HasProcedureMetadata(string endpointName) => _procedureMetadataCache.ContainsKey(endpointName);

    public virtual IEnumerable<string> GetCachedEndpoints() => _objectMetadataCache.Keys;

    public virtual IEnumerable<string> GetCachedProcedureEndpoints() => _procedureMetadataCache.Keys;

    public virtual void ClearAllMetadata()
    {
        _objectMetadataCache.Clear();
        _procedureMetadataCache.Clear();
        Log.Information("All SQL metadata cache cleared, will reload lazily on next request");
    }
}
