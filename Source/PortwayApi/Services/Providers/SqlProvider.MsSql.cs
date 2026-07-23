namespace PortwayApi.Services.Providers;

using System.Data.Common;
using Microsoft.Data.SqlClient;
using PortwayApi.Services.Database;
using SqlKata.Compilers;

public class MsSqlProvider : SqlProviderBase
{
    public override SqlProviderType ProviderType => SqlProviderType.SqlServer;
    public override bool SupportsTvf => true;

    public override DbConnection CreateConnection(string connectionString)
        => new SqlConnection(connectionString);

    public override string OptimizeConnectionString(string connectionString, SqlPoolingOptions options)
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

    public override Compiler GetCompiler() => new SqlServerCompiler();

    public override (string CommandText, System.Data.CommandType CommandType) BuildProcedureInvocation(
        string schema, string procedureName, IReadOnlyCollection<string> parameterNames)
        => ($"[{schema}].[{procedureName}]", System.Data.CommandType.StoredProcedure);

    public override string MapSqlTypeToClr(string nativeType) => nativeType.ToLowerInvariant() switch
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
        // timestamp is a rowversion synonym here, a binary version counter, not a date
        "timestamp" => "System.Byte[]",
        "rowversion" => "System.Byte[]",
        "sysname" => "System.String",
        "tinyint" => "System.Byte",
        "uniqueidentifier" => "System.Guid",
        "varbinary" => "System.Byte[]",
        "varchar" => "System.String",
        "xml" => "System.String",
        _ => "System.Object"
    };

    public override async Task<List<ColumnMetadata>> GetTvfColumnsAsync(
        DbConnection connection, string schema, string functionName, CancellationToken cancellationToken)
    {
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

        var columns = new List<ColumnMetadata>();
        using var command = CreateCommand(connection, query, ("@Schema", schema), ("@FunctionName", functionName));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var dataType = ReadString(reader, "DATA_TYPE");
            columns.Add(new ColumnMetadata
            {
                DatabaseColumnName = ReadString(reader, "COLUMN_NAME"),
                DataType = dataType,
                IsNullable = ReadBool(reader, "IS_NULLABLE"),
                MaxLength = ReadNullableInt(reader, "CHARACTER_MAXIMUM_LENGTH"),
                NumericPrecision = ReadNullableInt(reader, "NUMERIC_PRECISION"),
                NumericScale = ReadNullableInt(reader, "NUMERIC_SCALE"),
                ClrType = MapSqlTypeToClr(dataType)
            });
        }
        return columns;
    }

    public override async Task<List<ParameterMetadata>> GetProcedureParametersAsync(
        DbConnection connection, string schema, string procedureName, CancellationToken cancellationToken)
    {
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

        var parameters = new List<ParameterMetadata>();
        using var command = CreateCommand(connection, query, ("@Schema", schema), ("@ProcedureName", procedureName));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var dataType = ReadString(reader, "DATA_TYPE");
            parameters.Add(new ParameterMetadata
            {
                ParameterName = ReadString(reader, "PARAMETER_NAME"),
                DataType = dataType,
                IsNullable = ReadBool(reader, "IS_NULLABLE"),
                MaxLength = ReadNullableInt(reader, "MAX_LENGTH"),
                NumericPrecision = ReadNullableInt(reader, "NUMERIC_PRECISION"),
                NumericScale = ReadNullableInt(reader, "NUMERIC_SCALE"),
                IsOutput = ReadBool(reader, "IS_OUTPUT"),
                HasDefaultValue = ReadBool(reader, "HAS_DEFAULT_VALUE"),
                Position = ReadNullableInt(reader, "POSITION") ?? 0,
                ClrType = MapSqlTypeToClr(dataType)
            });
        }
        return parameters;
    }
}
