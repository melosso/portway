namespace PortwayApi.Services.Providers;

using System.Data;
using System.Data.Common;
using PortwayApi.Services.Database;
using Serilog;
using SqlKata.Compilers;

/// <summary>Shared provider plumbing: capability defaults, command helpers and ANSI information_schema metadata</summary>
public abstract class SqlProviderBase : ISqlProvider
{
    public abstract SqlProviderType ProviderType { get; }

    public virtual bool SupportsTvf => false;
    public virtual bool SupportsProcedures => true;
    public virtual bool SupportsSchemas => true;
    public virtual string DefaultSchema => "dbo";
    public virtual string HealthCheckQuery => "SELECT 1";

    public abstract DbConnection CreateConnection(string connectionString);
    public abstract string OptimizeConnectionString(string connectionString, SqlPoolingOptions options);
    public abstract Compiler GetCompiler();
    public abstract string MapSqlTypeToClr(string nativeType);

    // Plain schema.name with driver-handled StoredProcedure works for MySqlConnector and friends
    public virtual (string CommandText, CommandType CommandType) BuildProcedureInvocation(
        string schema, string procedureName, IReadOnlyCollection<string> parameterNames)
        => (schema.Length > 0 ? $"{schema}.{procedureName}" : procedureName, CommandType.StoredProcedure);

    public virtual Task<List<ColumnMetadata>> GetTvfColumnsAsync(
        DbConnection connection, string schema, string functionName, CancellationToken cancellationToken)
    {
        Log.Information("{Provider}: TVF column metadata is not available; {Schema}.{Function} works but its OpenAPI schema omits column details",
            ProviderType, schema, functionName);
        return Task.FromResult(new List<ColumnMetadata>());
    }

    public virtual Task<List<ParameterMetadata>> GetProcedureParametersAsync(
        DbConnection connection, string schema, string procedureName, CancellationToken cancellationToken)
    {
        Log.Information("{Provider}: procedure parameter metadata is not available; skipping {Schema}.{Procedure}",
            ProviderType, schema, procedureName);
        return Task.FromResult(new List<ParameterMetadata>());
    }

    // Bounded so a hung database cannot stall startup metadata initialization
    protected const int MetadataCommandTimeoutSeconds = 15;

    protected static DbCommand CreateCommand(DbConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = MetadataCommandTimeoutSeconds;
        foreach (var (name, value) in parameters)
        {
            var p = command.CreateParameter();
            p.ParameterName = name;
            p.Value = value;
            command.Parameters.Add(p);
        }
        return command;
    }

    protected static int? ReadNullableInt(DbDataReader reader, string column)
        => reader[column] != DBNull.Value ? Convert.ToInt32(reader[column]) : null;

    protected static bool ReadBool(DbDataReader reader, string column)
        => reader[column] != DBNull.Value && Convert.ToBoolean(reader[column]);

    protected static string ReadString(DbDataReader reader, string column)
        => reader[column]?.ToString() ?? string.Empty;

    /// <summary>ANSI information_schema.parameters lookup shared by PostgreSQL (pattern match) and MySQL (exact match)</summary>
    protected async Task<List<ParameterMetadata>> QueryInformationSchemaParametersAsync(
        DbConnection connection, string schema, string procedureName, bool patternMatch, CancellationToken cancellationToken)
    {
        // PostgreSQL suffixes specific_name with an oid, so callers there match by prefix
        var query = $@"
            SELECT parameter_name, data_type, parameter_mode, ordinal_position,
                   character_maximum_length, numeric_precision, numeric_scale
            FROM information_schema.parameters
            WHERE specific_schema = @schema
              AND specific_name {(patternMatch ? "LIKE" : "=")} @procname
              AND (parameter_mode = 'IN' OR parameter_mode IS NULL)
            ORDER BY ordinal_position";

        var parameters = new List<ParameterMetadata>();
        using var command = CreateCommand(connection, query,
            ("@schema", schema),
            ("@procname", patternMatch ? procedureName + "%" : procedureName));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var dataType = ReadString(reader, "data_type");
            parameters.Add(new ParameterMetadata
            {
                ParameterName = ReadString(reader, "parameter_name"),
                DataType = dataType,
                IsNullable = true,
                MaxLength = ReadNullableInt(reader, "character_maximum_length"),
                NumericPrecision = ReadNullableInt(reader, "numeric_precision"),
                NumericScale = ReadNullableInt(reader, "numeric_scale"),
                IsOutput = string.Equals(ReadString(reader, "parameter_mode"), "OUT", StringComparison.OrdinalIgnoreCase),
                HasDefaultValue = false,
                Position = ReadNullableInt(reader, "ordinal_position") ?? 0,
                ClrType = MapSqlTypeToClr(dataType)
            });
        }
        return parameters;
    }
}
