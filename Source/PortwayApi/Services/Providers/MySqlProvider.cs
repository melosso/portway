using System.Data.Common;
using MySqlConnector;
using PortwayApi.Classes;
using PortwayApi.Classes.Providers;
using Serilog;
using SqlKata.Compilers;

namespace PortwayApi.Services.Providers;

public class MySqlProvider : ISqlProvider
{
    public SqlProviderType ProviderType => SqlProviderType.MySql;
    public bool SupportsTvf => false;
    public bool SupportsProcedures => true;
    public bool SupportsSchemas => true;
    public string HealthCheckQuery => "SELECT 1";

    public DbConnection CreateConnection(string connectionString)
        => new MySqlConnection(connectionString);

    public string OptimizeConnectionString(string connectionString, SqlPoolingOptions options)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString)
        {
            MinimumPoolSize = (uint)options.MinPoolSize,
            MaximumPoolSize = (uint)options.MaxPoolSize,
            ConnectionTimeout = (uint)options.ConnectionTimeout,
            Pooling = options.EnablePooling,
            ApplicationName = options.ApplicationName
        };
        return builder.ConnectionString;
    }

    public Compiler GetCompiler() => new MySqlCompiler();

    public string MapSqlTypeToClr(string nativeType) => nativeType.ToLowerInvariant() switch
    {
        "int" or "mediumint" => "System.Int32",
        "bigint" => "System.Int64",
        "smallint" => "System.Int16",
        "tinyint" => "System.Byte",
        "tinyint(1)" or "boolean" or "bool" => "System.Boolean",
        "varchar" or "text" or "mediumtext" or "longtext" or "char" or "enum" or "set" => "System.String",
        "datetime" or "timestamp" => "System.DateTime",
        "date" => "System.DateTime",
        "time" => "System.TimeSpan",
        "decimal" or "numeric" => "System.Decimal",
        "float" => "System.Single",
        "double" or "real" => "System.Double",
        "blob" or "mediumblob" or "longblob" or "binary" or "varbinary" => "System.Byte[]",
        "json" => "System.String",
        _ => "System.Object"
    };

    public Task<List<ColumnMetadata>> GetTvfColumnsAsync(
        DbConnection connection, string schema, string functionName, CancellationToken cancellationToken)
    {
        Log.Information("MySQL does not support Table-Valued Functions; skipping TVF metadata for {Schema}.{Function}", schema, functionName);
        return Task.FromResult(new List<ColumnMetadata>());
    }

    public async Task<List<ParameterMetadata>> GetProcedureParametersAsync(
        DbConnection connection, string schema, string procedureName, CancellationToken cancellationToken)
    {
        var parameters = new List<ParameterMetadata>();

        const string query = @"
            SELECT
                PARAMETER_NAME,
                DATA_TYPE,
                PARAMETER_MODE,
                ORDINAL_POSITION,
                CHARACTER_MAXIMUM_LENGTH,
                NUMERIC_PRECISION,
                NUMERIC_SCALE
            FROM information_schema.parameters
            WHERE SPECIFIC_SCHEMA = @schema
              AND SPECIFIC_NAME = @procname
              AND (PARAMETER_MODE = 'IN' OR PARAMETER_MODE IS NULL)
            ORDER BY ORDINAL_POSITION";

        using var command = connection.CreateCommand();
        command.CommandText = query;

        var schemaParam = command.CreateParameter();
        schemaParam.ParameterName = "@schema";
        schemaParam.Value = schema;
        command.Parameters.Add(schemaParam);

        var nameParam = command.CreateParameter();
        nameParam.ParameterName = "@procname";
        nameParam.Value = procedureName;
        command.Parameters.Add(nameParam);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var dataType = reader["DATA_TYPE"]?.ToString() ?? string.Empty;
            var param = new ParameterMetadata
            {
                ParameterName = reader["PARAMETER_NAME"]?.ToString() ?? string.Empty,
                DataType = dataType,
                IsNullable = true,
                MaxLength = reader["CHARACTER_MAXIMUM_LENGTH"] != DBNull.Value
                    ? Convert.ToInt32(reader["CHARACTER_MAXIMUM_LENGTH"]) : null,
                NumericPrecision = reader["NUMERIC_PRECISION"] != DBNull.Value
                    ? Convert.ToInt32(reader["NUMERIC_PRECISION"]) : null,
                NumericScale = reader["NUMERIC_SCALE"] != DBNull.Value
                    ? Convert.ToInt32(reader["NUMERIC_SCALE"]) : null,
                IsOutput = false,
                HasDefaultValue = false,
                Position = reader["ORDINAL_POSITION"] != DBNull.Value ? Convert.ToInt32(reader["ORDINAL_POSITION"]) : 0
            };
            param.ClrType = MapSqlTypeToClr(dataType);
            parameters.Add(param);
        }

        return parameters;
    }
}
