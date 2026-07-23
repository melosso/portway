namespace PortwayApi.Services.Providers;

using System.Data.Common;
using MySqlConnector;
using PortwayApi.Services.Database;
using SqlKata.Compilers;

public class MySqlProvider : SqlProviderBase
{
    public override SqlProviderType ProviderType => SqlProviderType.MySql;
    // MySQL schemas are databases, resolve from the active connection
    public override string DefaultSchema => "";

    public override DbConnection CreateConnection(string connectionString)
        => new MySqlConnection(connectionString);

    public override string OptimizeConnectionString(string connectionString, SqlPoolingOptions options)
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

    public override Compiler GetCompiler() => new MySqlCompiler();

    public override string MapSqlTypeToClr(string nativeType) => nativeType.ToLowerInvariant() switch
    {
        "int" or "mediumint" or "year" => "System.Int32",
        "bigint" => "System.Int64",
        "smallint" => "System.Int16",
        "tinyint" => "System.Byte",
        "tinyint(1)" or "bit(1)" or "boolean" or "bool" => "System.Boolean",
        // information_schema reports plain DATA_TYPE, but COLUMN_TYPE strings may carry unsigned
        "tinyint unsigned" => "System.Byte",
        "smallint unsigned" => "System.UInt16",
        "mediumint unsigned" or "int unsigned" => "System.UInt32",
        "bigint unsigned" => "System.UInt64",
        "bit" => "System.UInt64",
        "varchar" or "text" or "tinytext" or "mediumtext" or "longtext" or "char" or "enum" or "set" => "System.String",
        "datetime" or "timestamp" => "System.DateTime",
        "date" => "System.DateTime",
        "time" => "System.TimeSpan",
        "decimal" or "numeric" => "System.Decimal",
        "float" => "System.Single",
        "double" or "real" => "System.Double",
        "blob" or "tinyblob" or "mediumblob" or "longblob" or "binary" or "varbinary" => "System.Byte[]",
        "geometry" or "point" or "linestring" or "polygon" or "multipoint"
            or "multilinestring" or "multipolygon" or "geometrycollection" => "System.Byte[]",
        "json" => "System.String",
        _ => "System.Object"
    };

    public override Task<List<ParameterMetadata>> GetProcedureParametersAsync(
        DbConnection connection, string schema, string procedureName, CancellationToken cancellationToken)
        => QueryInformationSchemaParametersAsync(connection, schema, procedureName, patternMatch: false, cancellationToken);
}
