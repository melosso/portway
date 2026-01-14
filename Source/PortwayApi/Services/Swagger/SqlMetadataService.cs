using System.Data;
using Microsoft.Data.SqlClient;
using Serilog;

namespace PortwayApi.Services;

public class SqlMetadataService
{
    private readonly Dictionary<string, List<ColumnMetadata>> _objectMetadataCache = new();
    private readonly Dictionary<string, List<ParameterMetadata>> _procedureMetadataCache = new();
    private readonly object _cacheLock = new();
    private readonly SqlConnectionPoolService _connectionPoolService;
    private bool _isInitialized = false;

    public SqlMetadataService(SqlConnectionPoolService connectionPoolService)
    {
        _connectionPoolService = connectionPoolService;
    }

    public class ColumnMetadata
    {
        public string ColumnName { get; set; } = string.Empty;
        public string DatabaseColumnName { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public string ClrType { get; set; } = string.Empty;
        public int? MaxLength { get; set; }
        public bool IsNullable { get; set; }
        public int? NumericPrecision { get; set; }
        public int? NumericScale { get; set; }
        public bool IsPrimaryKey { get; set; }
        public bool IsIdentity { get; set; }
        public bool IsComputed { get; set; }
    }

    public class ParameterMetadata
    {
        public string ParameterName { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public string ClrType { get; set; } = string.Empty;
        public int? MaxLength { get; set; }
        public bool IsNullable { get; set; }
        public int? NumericPrecision { get; set; }
        public int? NumericScale { get; set; }
        public bool IsOutput { get; set; }
        public bool HasDefaultValue { get; set; }
        public int Position { get; set; }
    }

    public async Task InitializeAsync(
        Dictionary<string, Classes.EndpointDefinition> sqlEndpoints,
        Func<string, Task<string>> getConnectionStringAsync)
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
            {
                shouldInitialize = true;
            }
        }

        if (!shouldInitialize)
            return;

        Log.Debug("Initializing SQL metadata cache for {Count} endpoints...", sqlEndpoints.Count);
        
        var initTasks = new List<Task>();
        
        foreach (var endpoint in sqlEndpoints)
        {
            var endpointName = endpoint.Key;
            var definition = endpoint.Value;
            
            if (string.IsNullOrEmpty(definition.DatabaseObjectName))
                continue;

            var environment = definition.AllowedEnvironments?.FirstOrDefault();
            if (environment == null)
            {
                Log.Warning("Endpoint {EndpointName} has no allowed environments, skipping metadata initialization", endpointName);
                continue;
            }

            initTasks.Add(InitializeEndpointMetadataAsync(
                endpointName, 
                definition, 
                environment, 
                getConnectionStringAsync));
        }

        try
        {
            await Task.WhenAll(initTasks);
        }
        catch (Exception ex)
        {
            var errorMessage = ex.Message;
            Log.Error(ex, "One or more metadata initialization tasks failed: {ErrorType} - {ErrorMessage}",
                ex.GetType().Name, errorMessage);
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
        string environment,
        Func<string, Task<string>> getConnectionStringAsync)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            
            var connectionString = await getConnectionStringAsync(environment);
            if (string.IsNullOrEmpty(connectionString))
            {
                Log.Warning("No connection string found for environment {Environment}, skipping endpoint {EndpointName}", 
                    environment, endpointName);
                return;
            }

            // Load object metadata for GET operations
            await LoadObjectMetadataForEndpointAsync(endpointName, definition, connectionString, cts.Token);
            
            // Load procedure metadata for POST, PUT, PATCH operations if procedure is defined
            if (HasModificationMethods(definition) && !string.IsNullOrEmpty(definition.Procedure))
            {
                await LoadProcedureMetadataForEndpointAsync(endpointName, definition, connectionString, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Log.Error("Metadata initialization timed out for endpoint {EndpointName} after 30 seconds", endpointName);
        }
        catch (Exception ex)
        {
            // Get the most relevant error message
            var errorMessage = ex.Message;
            Log.Error(ex, "Failed to initialize metadata for endpoint {EndpointName}: {ErrorType} - {ErrorMessage}",
                endpointName, ex.GetType().Name, errorMessage);
        }
    }

    private bool HasModificationMethods(Classes.EndpointDefinition definition)
    {
        // Check if the endpoint supports any modification methods by checking if Procedure is defined
        // and the endpoint is configured for write operations
        return !string.IsNullOrEmpty(definition.Procedure);
    }

    private async Task LoadObjectMetadataForEndpointAsync(
        string endpointName,
        Classes.EndpointDefinition definition,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var schema = definition.DatabaseSchema ?? "dbo";
            var objectName = definition.DatabaseObjectName!;
            var objectType = definition.DatabaseObjectType ?? "Table";
            var allowedColumns = definition.AllowedColumns ?? new List<string>();
            var primaryKey = definition.PrimaryKey;

            Log.Debug("Loading object metadata for {EndpointName}: {Schema}.{ObjectName} ({ObjectType})", 
                endpointName, schema, objectName, objectType);

            var optimizedConnectionString = _connectionPoolService.OptimizeConnectionString(connectionString);
            await using var connection = new SqlConnection(optimizedConnectionString);
            await connection.OpenAsync(cancellationToken);

            List<ColumnMetadata> columns;

            switch (objectType.ToLowerInvariant())
            {
                case "table":
                    columns = await GetTableColumnsAsync(connection, schema, objectName, cancellationToken).ConfigureAwait(false);
                    break;
                
                case "view":
                    columns = await GetViewColumnsAsync(connection, schema, objectName, cancellationToken).ConfigureAwait(false);
                    break;
                
                case "tablevaluedfunction":
                    columns = await GetTableValuedFunctionColumnsAsync(connection, schema, objectName, cancellationToken).ConfigureAwait(false);
                    break;
                
                default:
                    Log.Warning("Unknown database object type '{ObjectType}' for endpoint {EndpointName}", 
                        objectType, endpointName);
                    return;
            }

            // Mark primary key and apply column filtering/aliasing
            columns = ProcessColumnMetadata(columns, definition, endpointName);  // Assign the returned filtered list

            // Cache the object metadata
            lock (_cacheLock)
            {
                _objectMetadataCache[endpointName] = columns;  // Now caching the FILTERED columns
            }
            
            Log.Debug("Cached object metadata for {EndpointName}: {ColumnCount} columns", 
                endpointName, columns.Count);
        }
        catch (Exception ex)
        {
            var errorMessage = ex.Message;
            Log.Error(ex, "Error loading object metadata for endpoint {EndpointName}: {ErrorType} - {ErrorMessage}",
                endpointName, ex.GetType().Name, errorMessage);
            throw;
        }
    }

    private async Task LoadProcedureMetadataForEndpointAsync(
        string endpointName,
        Classes.EndpointDefinition definition,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var procedureName = definition.Procedure!;
            Log.Debug("Loading procedure metadata for {EndpointName}: {ProcedureName}", 
                endpointName, procedureName);

            var optimizedConnectionString = _connectionPoolService.OptimizeConnectionString(connectionString);
            await using var connection = new SqlConnection(optimizedConnectionString);
            await connection.OpenAsync(cancellationToken);

            // Parse schema and procedure name
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

            var parameters = await GetStoredProcedureParametersAsync(connection, schema, name, cancellationToken).ConfigureAwait(false);

            // Filter out reserved parameters like @Method
            parameters = parameters.Where(p => !IsReservedParameter(p.ParameterName)).ToList();

            // Cache the procedure metadata
            lock (_cacheLock)
            {
                _procedureMetadataCache[endpointName] = parameters;
            }

            Log.Debug("Cached procedure metadata for {EndpointName}: {ParameterCount} parameters", 
                endpointName, parameters.Count);
        }
        catch (Exception ex)
        {
            var errorMessage = ex.Message;
            Log.Error(ex, "Error loading procedure metadata for endpoint {EndpointName}: {ErrorType} - {ErrorMessage}",
                endpointName, ex.GetType().Name, errorMessage);
            throw;
        }
    }

    private bool IsReservedParameter(string parameterName)
    {
        var reservedParameters = new[] { "@method", "@action", "@operation" };
        return reservedParameters.Contains(parameterName.ToLowerInvariant());
    }

    private List<ColumnMetadata> ProcessColumnMetadata(List<ColumnMetadata> columns, Classes.EndpointDefinition definition, string endpointName)
    {
        var primaryKey = definition.PrimaryKey;
        var allowedColumns = definition.AllowedColumns ?? new List<string>();

        // Mark primary key column if specified
        if (!string.IsNullOrEmpty(primaryKey))
        {
            var pkColumn = columns.FirstOrDefault(c => 
                c.DatabaseColumnName.Equals(primaryKey, StringComparison.OrdinalIgnoreCase));
            if (pkColumn != null)
            {
                pkColumn.IsPrimaryKey = true;
            }
        }

        // Filter columns based on AllowedColumns if specified
        if (allowedColumns.Any())
        {
            var (aliasToDatabase, databaseToAlias) = 
                Classes.Helpers.ColumnMappingHelper.ParseColumnMappings(allowedColumns);

            // Build the set of allowed database column names
            var allowedDatabaseColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            // Add mapped columns from alias definitions
            foreach (var dbColumn in aliasToDatabase.Values)
            {
                allowedDatabaseColumns.Add(dbColumn);
            }
            
            // Add simple column names (without 'as' syntax)
            foreach (var columnDef in allowedColumns)
            {
                if (!columnDef.Contains(" as ", StringComparison.OrdinalIgnoreCase))
                {
                    var simpleColumn = columnDef.Trim();
                    allowedDatabaseColumns.Add(simpleColumn);
                    
                    // Also ensure this simple column exists in databaseToAlias for mapping
                    if (!databaseToAlias.ContainsKey(simpleColumn))
                    {
                        databaseToAlias[simpleColumn] = simpleColumn;
                    }
                }
            }

            // Filter columns - only keep those in allowed columns
            var filteredColumns = columns.Where(c => allowedDatabaseColumns.Contains(c.DatabaseColumnName)).ToList();

            // Apply proper column names and ensure no empty names
            foreach (var column in filteredColumns)
            {
                // Always start with the database column name as fallback
                string finalColumnName = column.DatabaseColumnName;

                // Try to get alias mapping
                if (databaseToAlias.TryGetValue(column.DatabaseColumnName, out var alias))
                {
                    finalColumnName = alias;
                }

                // Ensure we never have an empty column name
                if (string.IsNullOrWhiteSpace(finalColumnName))
                {
                    Log.Warning("Empty column name detected for database column {DatabaseColumn} in endpoint {EndpointName}",
                        column.DatabaseColumnName, endpointName);
                    finalColumnName = "UnknownColumn";
                }

                column.ColumnName = finalColumnName;
            }

            Log.Debug("Filtered to {Count} allowed columns for {EndpointName}", filteredColumns.Count, endpointName);
            
            // Debug: Log the final column names
            foreach (var column in filteredColumns)
            {
                Log.Debug("Final column mapping: {DatabaseName} -> {ColumnName}", 
                    column.DatabaseColumnName, column.ColumnName);
            }

            return filteredColumns;
        }
        else
        {
            foreach (var column in columns)
            {
                column.ColumnName = column.DatabaseColumnName;
            }
            
            return columns;
        }
    }

    private async Task<List<ParameterMetadata>> GetStoredProcedureParametersAsync(
        SqlConnection connection,
        string schema,
        string procedureName,
        CancellationToken cancellationToken)
    {
        var parameters = new List<ParameterMetadata>();

        // Query system views to get procedure parameters
        var query = @"
            SELECT 
                p.name AS PARAMETER_NAME,
                TYPE_NAME(p.user_type_id) AS DATA_TYPE,
                p.is_nullable AS IS_NULLABLE,
                p.max_length AS MAX_LENGTH,
                p.precision AS NUMERIC_PRECISION,
                p.scale AS NUMERIC_SCALE,
                p.is_output AS IS_OUTPUT,
                p.has_default_value AS HAS_DEFAULT_VALUE,
                p.parameter_id AS POSITION
            FROM sys.parameters p
            INNER JOIN sys.objects o ON p.object_id = o.object_id
            INNER JOIN sys.schemas s ON o.schema_id = s.schema_id
            WHERE o.type = 'P'  -- Stored procedures
                AND s.name = @Schema
                AND o.name = @ProcedureName
            ORDER BY p.parameter_id";

        await using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@Schema", schema);
        command.Parameters.AddWithValue("@ProcedureName", procedureName);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var parameter = new ParameterMetadata
            {
                ParameterName = reader["PARAMETER_NAME"].ToString() ?? string.Empty,
                DataType = reader["DATA_TYPE"].ToString() ?? string.Empty,
                IsNullable = reader["IS_NULLABLE"] != DBNull.Value && Convert.ToBoolean(reader["IS_NULLABLE"]),
                MaxLength = reader["MAX_LENGTH"] != DBNull.Value
                    ? Convert.ToInt32(reader["MAX_LENGTH"])
                    : null,
                NumericPrecision = reader["NUMERIC_PRECISION"] != DBNull.Value
                    ? Convert.ToInt32(reader["NUMERIC_PRECISION"])
                    : null,
                NumericScale = reader["NUMERIC_SCALE"] != DBNull.Value
                    ? Convert.ToInt32(reader["NUMERIC_SCALE"])
                    : null,
                IsOutput = reader["IS_OUTPUT"] != DBNull.Value && Convert.ToBoolean(reader["IS_OUTPUT"]),
                HasDefaultValue = reader["HAS_DEFAULT_VALUE"] != DBNull.Value && Convert.ToBoolean(reader["HAS_DEFAULT_VALUE"]),
                Position = reader["POSITION"] != DBNull.Value ? Convert.ToInt32(reader["POSITION"]) : 0
            };

            parameter.ClrType = MapSqlTypeToClrType(parameter.DataType);
            parameters.Add(parameter);
        }

        return parameters;
    }

    private async Task<List<ColumnMetadata>> GetTableColumnsAsync(
        SqlConnection connection, 
        string schema, 
        string tableName,
        CancellationToken cancellationToken)
    {
        var columns = new List<ColumnMetadata>();

        // Get columns using GetSchema
        var restrictions = new[] { null, schema, tableName, null };
        var columnsTable = await connection.GetSchemaAsync("Columns", restrictions, cancellationToken);

        // Log all columns found
        Log.Debug("Found {Count} columns for {Schema}.{TableName}:", 
            columnsTable.Rows.Count, schema, tableName);
        
        foreach (DataRow row in columnsTable.Rows)
        {
            var columnName = row["COLUMN_NAME"].ToString() ?? string.Empty;
            Log.Debug("  - Column: '{ColumnName}', DataType: {DataType}, Nullable: {IsNullable}", 
                columnName, 
                row["DATA_TYPE"].ToString() ?? "unknown",
                row["IS_NULLABLE"].ToString() ?? "unknown");
                
            var column = new ColumnMetadata
            {
                DatabaseColumnName = columnName,
                DataType = row["DATA_TYPE"].ToString() ?? string.Empty,
                IsNullable = row["IS_NULLABLE"].ToString()?.Equals("YES", StringComparison.OrdinalIgnoreCase) ?? false,
                MaxLength = row["CHARACTER_MAXIMUM_LENGTH"] != DBNull.Value 
                    ? Convert.ToInt32(row["CHARACTER_MAXIMUM_LENGTH"]) 
                    : null,
                NumericPrecision = row["NUMERIC_PRECISION"] != DBNull.Value 
                    ? Convert.ToInt32(row["NUMERIC_PRECISION"]) 
                    : null,
                NumericScale = row["NUMERIC_SCALE"] != DBNull.Value 
                    ? Convert.ToInt32(row["NUMERIC_SCALE"]) 
                    : null
            };

            // Map SQL type to CLR type
            column.ClrType = MapSqlTypeToClrType(column.DataType);

            columns.Add(column);
        }

        return columns;
    }
    private async Task<List<ColumnMetadata>> GetViewColumnsAsync(
        SqlConnection connection, 
        string schema, 
        string viewName,
        CancellationToken cancellationToken)
    {
        // Views use the same Columns schema collection as tables
        return await GetTableColumnsAsync(connection, schema, viewName, cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<ColumnMetadata>> GetTableValuedFunctionColumnsAsync(
        SqlConnection connection, 
        string schema, 
        string functionName,
        CancellationToken cancellationToken)
    {
        var columns = new List<ColumnMetadata>();

        // For table-valued functions, we need to query system views
        var query = @"
            SELECT 
                c.name AS COLUMN_NAME,
                TYPE_NAME(c.user_type_id) AS DATA_TYPE,
                c.is_nullable AS IS_NULLABLE,
                c.max_length AS CHARACTER_MAXIMUM_LENGTH,
                c.precision AS NUMERIC_PRECISION,
                c.scale AS NUMERIC_SCALE
            FROM sys.objects o
            INNER JOIN sys.columns c ON o.object_id = c.object_id
            WHERE o.type IN ('IF', 'TF')  -- Inline and Table-valued functions
                AND SCHEMA_NAME(o.schema_id) = @Schema
                AND o.name = @FunctionName
            ORDER BY c.column_id";

        await using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@Schema", schema);
        command.Parameters.AddWithValue("@FunctionName", functionName);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var column = new ColumnMetadata
            {
                DatabaseColumnName = reader["COLUMN_NAME"].ToString() ?? string.Empty,
                DataType = reader["DATA_TYPE"].ToString() ?? string.Empty,
                IsNullable = reader["IS_NULLABLE"] != DBNull.Value && Convert.ToBoolean(reader["IS_NULLABLE"]),
                MaxLength = reader["CHARACTER_MAXIMUM_LENGTH"] != DBNull.Value 
                    ? Convert.ToInt32(reader["CHARACTER_MAXIMUM_LENGTH"]) 
                    : null,
                NumericPrecision = reader["NUMERIC_PRECISION"] != DBNull.Value 
                    ? Convert.ToInt32(reader["NUMERIC_PRECISION"]) 
                    : null,
                NumericScale = reader["NUMERIC_SCALE"] != DBNull.Value 
                    ? Convert.ToInt32(reader["NUMERIC_SCALE"]) 
                    : null
            };

            column.ClrType = MapSqlTypeToClrType(column.DataType);
            columns.Add(column);
        }

        return columns;
    }

    // Metadata retrieval methods
    public List<ColumnMetadata>? GetObjectMetadata(string endpointName)
    {
        lock (_cacheLock)
        {
            return _objectMetadataCache.TryGetValue(endpointName, out var metadata) 
                ? metadata 
                : null;
        }
    }

    public List<ParameterMetadata>? GetProcedureMetadata(string endpointName)
    {
        lock (_cacheLock)
        {
            return _procedureMetadataCache.TryGetValue(endpointName, out var metadata) 
                ? metadata 
                : null;
        }
    }

    public bool HasObjectMetadata(string endpointName)
    {
        lock (_cacheLock)
        {
            return _objectMetadataCache.ContainsKey(endpointName);
        }
    }

    public bool HasProcedureMetadata(string endpointName)
    {
        lock (_cacheLock)
        {
            return _procedureMetadataCache.ContainsKey(endpointName);
        }
    }

    /// <summary>
    /// Gets all cached endpoint names that have object metadata
    /// </summary>
    public IEnumerable<string> GetCachedEndpoints()
    {
        lock (_cacheLock)
        {
            return _objectMetadataCache.Keys.ToList();
        }
    }

    /// <summary>
    /// Gets all cached endpoint names that have procedure metadata
    /// </summary>
    public IEnumerable<string> GetCachedProcedureEndpoints()
    {
        lock (_cacheLock)
        {
            return _procedureMetadataCache.Keys.ToList();
        }
    }

    /// <summary>
    /// Maps SQL Server data types to .NET CLR types
    /// </summary>
    private string MapSqlTypeToClrType(string sqlType)
    {
        return sqlType.ToLowerInvariant() switch
        {
            "bigint" => "System.Int64",
            "binary" => "System.Byte[]",
            "bit" => "System.Boolean",
            "char" => "System.String",
            "date" => "System.DateTime",
            "datetime" => "System.DateTime",
            "datetime2" => "System.DateTime",
            "datetimeoffset" => "System.DateTimeOffset",
            "decimal" => "System.Decimal",
            "float" => "System.Double",
            "image" => "System.Byte[]",
            "int" => "System.Int32",
            "money" => "System.Decimal",
            "nchar" => "System.String",
            "ntext" => "System.String",
            "numeric" => "System.Decimal",
            "nvarchar" => "System.String",
            "real" => "System.Single",
            "smalldatetime" => "System.DateTime",
            "smallint" => "System.Int16",
            "smallmoney" => "System.Decimal",
            "sql_variant" => "System.Object",
            "text" => "System.String",
            "time" => "System.TimeSpan",
            "timestamp" => "System.Byte[]",
            "tinyint" => "System.Byte",
            "uniqueidentifier" => "System.Guid",
            "varbinary" => "System.Byte[]",
            "varchar" => "System.String",
            "xml" => "System.String",
            _ => "System.Object"
        };
    }

    /// <summary>
    /// Clears all cached metadata - forces lazy reload on next access
    /// </summary>
    public void ClearAllMetadata()
    {
        lock (_cacheLock)
        {
            _objectMetadataCache.Clear();
            _procedureMetadataCache.Clear();
        }

        Log.Information("All SQL metadata cache cleared, will reload lazily on next request");
    }

    /// <summary>
    /// Clears metadata for a specific endpoint - forces lazy reload on next access
    /// </summary>
    public void ClearEndpointMetadata(string endpointName)
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
            {
                _objectMetadataCache.Remove(key);
            }

            foreach (var key in procedureKeysToRemove)
            {
                _procedureMetadataCache.Remove(key);
            }

            Log.Debug("SQL metadata cache cleared for endpoint: {Endpoint} (Objects: {ObjectCount}, Procedures: {ProcedureCount})",
                endpointName, objectKeysToRemove.Count, procedureKeysToRemove.Count);
        }
    }
}