using System.Data;
using System.Data.Common;
using PortwayApi.Classes;
using PortwayApi.Classes.Providers;
using PortwayApi.Services.Providers;
using Serilog;

namespace PortwayApi.Services;

public class SqlMetadataService
{
    private readonly Dictionary<string, List<Classes.ColumnMetadata>> _objectMetadataCache = new();
    private readonly Dictionary<string, List<Classes.ParameterMetadata>> _procedureMetadataCache = new();
    private readonly object _cacheLock = new();
    private readonly SqlConnectionPoolService _connectionPoolService;
    private readonly ISqlProviderFactory _providerFactory;
    private bool _isInitialized = false;

    public SqlMetadataService(SqlConnectionPoolService connectionPoolService, ISqlProviderFactory providerFactory)
    {
        _connectionPoolService = connectionPoolService;
        _providerFactory = providerFactory;
    }

    public virtual async Task InitializeAsync(
        Dictionary<string, Classes.EndpointDefinition> sqlEndpoints,
        EnvironmentSettings environmentSettings,
        Func<string, Task<string>> getConnectionStringAsync,
        CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
        {
            Log.Warning("SqlMetadataService is already initialized. Skipping re-initialization.");
            return;
        }

        bool shouldInitialize = false;
        lock (_cacheLock)
        {
            if (!_isInitialized)
                shouldInitialize = true;
        }

        if (!shouldInitialize)
            return;

        Log.Debug("Initializing SQL metadata cache for {Count} endpoints...", sqlEndpoints.Count);

        var initTasks = new List<Task>();
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

        try
        {
            await Task.WhenAll(initTasks);
        }
        catch (Exception ex)
        {
            Log.Error("One or more metadata initialization tasks failed: {ErrorType} - {ErrorMessage}",
                ex.GetType().Name, ex.Message);
        }

        lock (_cacheLock)
        {
            _isInitialized = true;
        }

        Log.Debug("SQL metadata cache initialized successfully. Objects: {ObjectCount}, Procedures: {ProcedureCount}",
            _objectMetadataCache.Count, _procedureMetadataCache.Count);
    }

    private async Task InitializeEndpointMetadataAsync(
        string endpointName,
        Classes.EndpointDefinition definition,
        List<string> environments,
        Func<string, Task<string>> getConnectionStringAsync,
        CancellationToken cancellationToken = default)
    {
        foreach (var environment in environments)
        {
            try
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

                var connectionString = await getConnectionStringAsync(environment);
                if (string.IsNullOrEmpty(connectionString))
                {
                    Log.Debug("No connection string found for environment {Environment}, trying next environment for {EndpointName}",
                        environment, endpointName);
                    continue;
                }

                int columnCount = await LoadObjectMetadataForEndpointAsync(endpointName, definition, connectionString, cts.Token);

                if (columnCount == 0)
                {
                    Log.Debug("No columns found for {EndpointName} in environment {Environment}, trying next environment",
                        endpointName, environment);
                    continue;
                }

                if (HasModificationMethods(definition) && !string.IsNullOrEmpty(definition.Procedure))
                    await LoadProcedureMetadataForEndpointAsync(endpointName, definition, connectionString, cts.Token);

                Log.Debug("Successfully initialized metadata for {EndpointName} using environment {Environment} ({ColumnCount} columns)",
                    endpointName, environment, columnCount);
                return;
            }
            catch (OperationCanceledException)
            {
                Log.Error("Metadata initialization timed out for endpoint {EndpointName} in environment {Environment} after 30 seconds",
                    endpointName, environment);
            }
            catch (Exception ex)
            {
                Log.Warning("Failed to initialize metadata for endpoint {EndpointName} in environment {Environment}: {ErrorMessage}. Trying next environment...",
                    endpointName, environment, ex.Message);
            }
        }

        Log.Error("Failed to initialize metadata for endpoint {EndpointName} after trying all allowed environments: {Environments}",
            endpointName, string.Join(", ", environments));
    }

    private bool HasModificationMethods(Classes.EndpointDefinition definition)
        => !string.IsNullOrEmpty(definition.Procedure);

    private async Task<int> LoadObjectMetadataForEndpointAsync(
        string endpointName,
        Classes.EndpointDefinition definition,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var schema = definition.DatabaseSchema ?? "dbo";
        var objectName = definition.DatabaseObjectName!;
        var objectType = definition.DatabaseObjectType ?? "Table";

        Log.Debug("Loading object metadata for {EndpointName}: {Schema}.{ObjectName} ({ObjectType})",
            endpointName, schema, objectName, objectType);

        var provider = _providerFactory.GetProvider(connectionString);
        var optimizedConnectionString = _connectionPoolService.OptimizeConnectionString(connectionString);
        await using var connection = provider.CreateConnection(optimizedConnectionString);
        await connection.OpenAsync(cancellationToken);

        List<Classes.ColumnMetadata> columns;

        switch (objectType.ToLowerInvariant())
        {
            case "table":
            case "view":
                columns = await GetTableColumnsAsync(connection, provider, schema, objectName, cancellationToken).ConfigureAwait(false);
                break;

            case "tablevaluedfunction":
                if (!provider.SupportsTvf)
                {
                    Log.Information("Provider {Provider} does not support TVF; skipping column metadata for {EndpointName}", provider.ProviderType, endpointName);
                    return 0;
                }
                columns = await provider.GetTvfColumnsAsync(connection, schema, objectName, cancellationToken).ConfigureAwait(false);
                break;

            default:
                Log.Warning("Unknown database object type '{ObjectType}' for endpoint {EndpointName}", objectType, endpointName);
                return 0;
        }

        columns = ProcessColumnMetadata(columns, definition, endpointName);

        lock (_cacheLock)
        {
            _objectMetadataCache[endpointName] = columns;
        }

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
            schema = parts[0];
            name = parts[1];
        }
        else
        {
            schema = "dbo";
            name = procedureName;
        }

        var parameters = await provider.GetProcedureParametersAsync(connection, schema, name, cancellationToken).ConfigureAwait(false);
        parameters = parameters.Where(p => !IsReservedParameter(p.ParameterName)).ToList();

        lock (_cacheLock)
        {
            _procedureMetadataCache[endpointName] = parameters;
        }

        Log.Debug("Cached procedure metadata for {EndpointName}: {ParameterCount} parameters", endpointName, parameters.Count);
    }

    private bool IsReservedParameter(string parameterName)
    {
        var reservedParameters = new[] { "@method", "@action", "@operation" };
        return reservedParameters.Contains(parameterName.ToLowerInvariant());
    }

    private List<Classes.ColumnMetadata> ProcessColumnMetadata(List<Classes.ColumnMetadata> columns, Classes.EndpointDefinition definition, string endpointName)
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
                Classes.Helpers.ColumnMappingHelper.ParseColumnMappings(allowedColumns);

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
    /// Gets table/view columns using ADO.NET GetSchemaAsync first; falls back to provider-specific PRAGMA for SQLite.
    /// </summary>
    private async Task<List<Classes.ColumnMetadata>> GetTableColumnsAsync(
        DbConnection connection,
        ISqlProvider provider,
        string schema,
        string tableName,
        CancellationToken cancellationToken)
    {
        // SQLite uses PRAGMA, GetSchemaAsync("Columns") is not reliably supported
        if (provider is SqliteProvider sqliteProvider)
            return await sqliteProvider.GetColumnsViaPragmaAsync(connection, tableName, cancellationToken).ConfigureAwait(false);

        var columns = new List<Classes.ColumnMetadata>();

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

            var col = new Classes.ColumnMetadata
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

    public List<Classes.ColumnMetadata>? GetObjectMetadata(string endpointName)
    {
        lock (_cacheLock)
            return _objectMetadataCache.TryGetValue(endpointName, out var metadata) ? metadata : null;
    }

    public List<Classes.ParameterMetadata>? GetProcedureMetadata(string endpointName)
    {
        lock (_cacheLock)
            return _procedureMetadataCache.TryGetValue(endpointName, out var metadata) ? metadata : null;
    }

    public bool HasObjectMetadata(string endpointName)
    {
        lock (_cacheLock)
            return _objectMetadataCache.ContainsKey(endpointName);
    }

    public bool HasProcedureMetadata(string endpointName)
    {
        lock (_cacheLock)
            return _procedureMetadataCache.ContainsKey(endpointName);
    }

    public virtual IEnumerable<string> GetCachedEndpoints()
    {
        lock (_cacheLock)
            return _objectMetadataCache.Keys.ToList();
    }

    public virtual IEnumerable<string> GetCachedProcedureEndpoints()
    {
        lock (_cacheLock)
            return _procedureMetadataCache.Keys.ToList();
    }

    public virtual void ClearAllMetadata()
    {
        lock (_cacheLock)
        {
            _objectMetadataCache.Clear();
            _procedureMetadataCache.Clear();
        }
        Log.Information("All SQL metadata cache cleared, will reload lazily on next request");
    }

    public virtual void ClearEndpointMetadata(string endpointName)
    {
        lock (_cacheLock)
        {
            var objectKeysToRemove = _objectMetadataCache.Keys
                .Where(k => k.StartsWith($"{endpointName}:"))
                .ToList();

            var procedureKeysToRemove = _procedureMetadataCache.Keys
                .Where(k => k.StartsWith($"{endpointName}:"))
                .ToList();

            foreach (var key in objectKeysToRemove)
                _objectMetadataCache.Remove(key);

            foreach (var key in procedureKeysToRemove)
                _procedureMetadataCache.Remove(key);

            Log.Debug("SQL metadata cache cleared for endpoint: {Endpoint} (Objects: {ObjectCount}, Procedures: {ProcedureCount})",
                endpointName, objectKeysToRemove.Count, procedureKeysToRemove.Count);
        }
    }
}
