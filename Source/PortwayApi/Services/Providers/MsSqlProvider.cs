using System.Data.Common;
using Microsoft.Data.SqlClient;
using PortwayApi.Classes;
using PortwayApi.Classes.Providers;
using Serilog;
using SqlKata.Compilers;

namespace PortwayApi.Services.Providers;

public class MsSqlProvider : ISqlProvider
{
    public SqlProviderType ProviderType => SqlProviderType.SqlServer;
    public bool SupportsTvf => true;
    public bool SupportsProcedures => true;
    public bool SupportsSchemas => true;
    public string HealthCheckQuery => "SELECT 1";

    public DbConnection CreateConnection(string connectionString)
        => new SqlConnection(connectionString);

    public string OptimizeConnectionString(string connectionString, SqlPoolingOptions options)
    {
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            MinPoolSize = options.MinPoolSize,
            MaxPoolSize = options.MaxPoolSize,
            ConnectTimeout = options.ConnectionTimeout,
            Pooling = options.EnablePooling,
            ApplicationName = options.ApplicationName
        };
        return builder.ConnectionString;
    }

    public Compiler GetCompiler() => new SqlServerCompiler();

    public string MapSqlTypeToClr(string nativeType) => nativeType.ToLowerInvariant() switch
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

    public async Task<List<ColumnMetadata>> GetTvfColumnsAsync(
        DbConnection connection, string schema, string functionName, CancellationToken cancellationToken)
    {
        var columns = new List<ColumnMetadata>();

        const string query = @"
            SELECT
                c.name AS COLUMN_NAME,
                TYPE_NAME(c.user_type_id) AS DATA_TYPE,
                c.is_nullable AS IS_NULLABLE,
                c.max_length AS CHARACTER_MAXIMUM_LENGTH,
                c.precision AS NUMERIC_PRECISION,
                c.scale AS NUMERIC_SCALE
            FROM sys.objects o
            INNER JOIN sys.columns c ON o.object_id = c.object_id
            WHERE o.type IN ('IF', 'TF')
                AND SCHEMA_NAME(o.schema_id) = @Schema
                AND o.name = @FunctionName
            ORDER BY c.column_id";

        using var command = connection.CreateCommand();
        command.CommandText = query;

        var schemaParam = command.CreateParameter();
        schemaParam.ParameterName = "@Schema";
        schemaParam.Value = schema;
        command.Parameters.Add(schemaParam);

        var nameParam = command.CreateParameter();
        nameParam.ParameterName = "@FunctionName";
        nameParam.Value = functionName;
        command.Parameters.Add(nameParam);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var col = new ColumnMetadata
            {
                DatabaseColumnName = reader["COLUMN_NAME"].ToString() ?? string.Empty,
                DataType = reader["DATA_TYPE"].ToString() ?? string.Empty,
                IsNullable = reader["IS_NULLABLE"] != DBNull.Value && Convert.ToBoolean(reader["IS_NULLABLE"]),
                MaxLength = reader["CHARACTER_MAXIMUM_LENGTH"] != DBNull.Value
                    ? Convert.ToInt32(reader["CHARACTER_MAXIMUM_LENGTH"]) : null,
                NumericPrecision = reader["NUMERIC_PRECISION"] != DBNull.Value
                    ? Convert.ToInt32(reader["NUMERIC_PRECISION"]) : null,
                NumericScale = reader["NUMERIC_SCALE"] != DBNull.Value
                    ? Convert.ToInt32(reader["NUMERIC_SCALE"]) : null
            };
            col.ClrType = MapSqlTypeToClr(col.DataType);
            columns.Add(col);
        }

        return columns;
    }

    public async Task<List<ParameterMetadata>> GetProcedureParametersAsync(
        DbConnection connection, string schema, string procedureName, CancellationToken cancellationToken)
    {
        var parameters = new List<ParameterMetadata>();

        const string query = @"
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
            WHERE o.type = 'P'
                AND s.name = @Schema
                AND o.name = @ProcedureName
            ORDER BY p.parameter_id";

        using var command = connection.CreateCommand();
        command.CommandText = query;

        var schemaParam = command.CreateParameter();
        schemaParam.ParameterName = "@Schema";
        schemaParam.Value = schema;
        command.Parameters.Add(schemaParam);

        var nameParam = command.CreateParameter();
        nameParam.ParameterName = "@ProcedureName";
        nameParam.Value = procedureName;
        command.Parameters.Add(nameParam);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var param = new ParameterMetadata
            {
                ParameterName = reader["PARAMETER_NAME"].ToString() ?? string.Empty,
                DataType = reader["DATA_TYPE"].ToString() ?? string.Empty,
                IsNullable = reader["IS_NULLABLE"] != DBNull.Value && Convert.ToBoolean(reader["IS_NULLABLE"]),
                MaxLength = reader["MAX_LENGTH"] != DBNull.Value ? Convert.ToInt32(reader["MAX_LENGTH"]) : null,
                NumericPrecision = reader["NUMERIC_PRECISION"] != DBNull.Value ? Convert.ToInt32(reader["NUMERIC_PRECISION"]) : null,
                NumericScale = reader["NUMERIC_SCALE"] != DBNull.Value ? Convert.ToInt32(reader["NUMERIC_SCALE"]) : null,
                IsOutput = reader["IS_OUTPUT"] != DBNull.Value && Convert.ToBoolean(reader["IS_OUTPUT"]),
                HasDefaultValue = reader["HAS_DEFAULT_VALUE"] != DBNull.Value && Convert.ToBoolean(reader["HAS_DEFAULT_VALUE"]),
                Position = reader["POSITION"] != DBNull.Value ? Convert.ToInt32(reader["POSITION"]) : 0
            };
            param.ClrType = MapSqlTypeToClr(param.DataType);
            parameters.Add(param);
        }

        return parameters;
    }
}
