using System.Data.Common;
using Npgsql;
using PortwayApi.Classes;
using PortwayApi.Classes.Providers;
using Serilog;
using SqlKata.Compilers;

namespace PortwayApi.Services.Providers;

public class PostgreSqlProvider : ISqlProvider
{
    public SqlProviderType ProviderType => SqlProviderType.PostgreSql;
    public bool SupportsTvf => true;
    public bool SupportsProcedures => true;
    public bool SupportsSchemas => true;
    public string HealthCheckQuery => "SELECT 1";

    public DbConnection CreateConnection(string connectionString)
        => new NpgsqlConnection(connectionString);

    public string OptimizeConnectionString(string connectionString, SqlPoolingOptions options)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            MinPoolSize = options.MinPoolSize,
            MaxPoolSize = options.MaxPoolSize,
            ConnectionIdleLifetime = options.ConnectionTimeout,
            Pooling = options.EnablePooling,
            ApplicationName = options.ApplicationName
        };
        return builder.ConnectionString;
    }

    public Compiler GetCompiler() => new PostgresCompiler();

    public string MapSqlTypeToClr(string nativeType) => nativeType.ToLowerInvariant() switch
    {
        "integer" or "int4" or "serial" => "System.Int32",
        "bigint" or "int8" or "bigserial" => "System.Int64",
        "smallint" or "int2" => "System.Int16",
        "text" or "varchar" or "character varying" or "char" or "character" or "name" => "System.String",
        "boolean" or "bool" => "System.Boolean",
        "uuid" => "System.Guid",
        "timestamp" or "timestamp without time zone" or "timestamp with time zone" or "timestamptz" => "System.DateTime",
        "date" => "System.DateTime",
        "time" or "time without time zone" => "System.TimeSpan",
        "numeric" or "decimal" => "System.Decimal",
        "real" or "float4" => "System.Single",
        "double precision" or "float8" => "System.Double",
        "money" => "System.Decimal",
        "bytea" => "System.Byte[]",
        "json" or "jsonb" or "xml" => "System.String",
        "interval" => "System.TimeSpan",
        _ => "System.Object"
    };

    public Task<List<ColumnMetadata>> GetTvfColumnsAsync(
        DbConnection connection, string schema, string functionName, CancellationToken cancellationToken)
    {
        // PostgreSQL set-returning function metadata requires complex pg_catalog queries.
        // Return empty list so the endpoint still works; OpenAPI schema will be generated without column info.
        Log.Information("PostgreSQL TVF column metadata inspection not implemented for {Schema}.{Function}; endpoint will work but OpenAPI schema will be missing column details", schema, functionName);
        return Task.FromResult(new List<ColumnMetadata>());
    }

    public async Task<List<ParameterMetadata>> GetProcedureParametersAsync(
        DbConnection connection, string schema, string procedureName, CancellationToken cancellationToken)
    {
        var parameters = new List<ParameterMetadata>();

        // Use information_schema.parameters (works for PostgreSQL functions/procedures)
        const string query = @"
            SELECT
                p.parameter_name,
                p.data_type,
                p.parameter_mode,
                p.ordinal_position,
                p.character_maximum_length,
                p.numeric_precision,
                p.numeric_scale
            FROM information_schema.parameters p
            WHERE p.specific_schema = @schema
              AND p.specific_name LIKE @procname
              AND (p.parameter_mode = 'IN' OR p.parameter_mode IS NULL)
            ORDER BY p.ordinal_position";

        using var command = connection.CreateCommand();
        command.CommandText = query;

        var schemaParam = command.CreateParameter();
        schemaParam.ParameterName = "@schema";
        schemaParam.Value = schema;
        command.Parameters.Add(schemaParam);

        var nameParam = command.CreateParameter();
        nameParam.ParameterName = "@procname";
        nameParam.Value = procedureName + "%";
        command.Parameters.Add(nameParam);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var dataType = reader["data_type"]?.ToString() ?? string.Empty;
            var param = new ParameterMetadata
            {
                ParameterName = reader["parameter_name"]?.ToString() ?? string.Empty,
                DataType = dataType,
                IsNullable = true,
                MaxLength = reader["character_maximum_length"] != DBNull.Value
                    ? Convert.ToInt32(reader["character_maximum_length"]) : null,
                NumericPrecision = reader["numeric_precision"] != DBNull.Value
                    ? Convert.ToInt32(reader["numeric_precision"]) : null,
                NumericScale = reader["numeric_scale"] != DBNull.Value
                    ? Convert.ToInt32(reader["numeric_scale"]) : null,
                IsOutput = reader["parameter_mode"]?.ToString()?.Equals("OUT", StringComparison.OrdinalIgnoreCase) ?? false,
                HasDefaultValue = false,
                Position = reader["ordinal_position"] != DBNull.Value ? Convert.ToInt32(reader["ordinal_position"]) : 0
            };
            param.ClrType = MapSqlTypeToClr(dataType);
            parameters.Add(param);
        }

        return parameters;
    }
}
