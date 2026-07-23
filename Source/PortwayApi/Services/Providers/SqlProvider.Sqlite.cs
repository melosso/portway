namespace PortwayApi.Services.Providers;

using System.Data.Common;
using Microsoft.Data.Sqlite;
using PortwayApi.Services.Database;
using SqlKata.Compilers;

public class SqliteProvider : SqlProviderBase
{
    public override SqlProviderType ProviderType => SqlProviderType.Sqlite;
    public override bool SupportsProcedures => false;
    public override bool SupportsSchemas => false;
    public override string DefaultSchema => "";

    public override DbConnection CreateConnection(string connectionString)
        => new SqliteConnection(connectionString);

    // SQLite pooling is handled by the driver, the connection string passes through unchanged
    public override string OptimizeConnectionString(string connectionString, SqlPoolingOptions options)
        => connectionString;

    public override Compiler GetCompiler() => new SqliteCompiler();

    public override string MapSqlTypeToClr(string nativeType) => nativeType.ToUpperInvariant() switch
    {
        // INTEGER columns store 64-bit values (rowid/autoincrement keys), plain INT stays Int32
        "INTEGER" or "BIGINT" => "System.Int64",
        "INT" or "MEDIUMINT" => "System.Int32",
        "SMALLINT" => "System.Int16",
        "TINYINT" => "System.Byte",
        "REAL" or "FLOAT" or "DOUBLE" => "System.Double",
        "TEXT" or "VARCHAR" or "NVARCHAR" or "CHAR" or "NCHAR" or "CLOB" => "System.String",
        "BLOB" or "RAW" => "System.Byte[]",
        "NUMERIC" or "DECIMAL" => "System.Decimal",
        "BOOLEAN" or "BOOL" => "System.Boolean",
        "DATE" or "DATETIME" => "System.DateTime",
        "GUID" or "UUID" or "UNIQUEIDENTIFIER" => "System.Guid",
        _ => "System.Object"
    };

    /// <summary>Gets column metadata for a SQLite table using PRAGMA table_info</summary>
    public async Task<List<ColumnMetadata>> GetColumnsViaPragmaAsync(
        DbConnection connection, string tableName, CancellationToken cancellationToken)
    {
        var columns = new List<ColumnMetadata>();

        // PRAGMA table_info returns: cid, name, type, notnull, dflt_value, pk
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{tableName.Replace("\"", "\"\"")}\")";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var dataType = ReadString(reader, "type");
            columns.Add(new ColumnMetadata
            {
                DatabaseColumnName = ReadString(reader, "name"),
                DataType = dataType.Length > 0 ? dataType : "TEXT",
                IsNullable = ReadNullableInt(reader, "notnull") == 0,
                IsPrimaryKey = ReadNullableInt(reader, "pk") > 0,
                ClrType = MapSqlTypeToClr(dataType)
            });
        }
        return columns;
    }
}
