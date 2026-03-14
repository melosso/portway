using System.Data.Common;
using Microsoft.Data.Sqlite;
using PortwayApi.Classes;
using PortwayApi.Classes.Providers;
using Serilog;
using SqlKata.Compilers;

namespace PortwayApi.Services.Providers;

public class SqliteProvider : ISqlProvider
{
    public SqlProviderType ProviderType => SqlProviderType.Sqlite;
    public bool SupportsTvf => false;
    public bool SupportsProcedures => false;
    public bool SupportsSchemas => false;
    public string HealthCheckQuery => "SELECT 1";

    public DbConnection CreateConnection(string connectionString)
        => new SqliteConnection(connectionString);

    public string OptimizeConnectionString(string connectionString, SqlPoolingOptions options)
        => connectionString; // SQLite pooling is limited; return as-is

    public Compiler GetCompiler() => new SqliteCompiler();

    public string MapSqlTypeToClr(string nativeType) => nativeType.ToUpperInvariant() switch
    {
        "INTEGER" or "INT" => "System.Int32",
        "REAL" or "FLOAT" or "DOUBLE" => "System.Double",
        "TEXT" or "VARCHAR" or "CHAR" or "CLOB" => "System.String",
        "BLOB" => "System.Byte[]",
        "NUMERIC" or "DECIMAL" => "System.Decimal",
        "BOOLEAN" => "System.Boolean",
        "DATE" or "DATETIME" => "System.DateTime",
        _ => "System.Object"
    };

    public async Task<List<ColumnMetadata>> GetTvfColumnsAsync(
        DbConnection connection, string schema, string functionName, CancellationToken cancellationToken)
    {
        Log.Information("SQLite does not support Table-Valued Functions; skipping TVF metadata for {Function}", functionName);
        return new List<ColumnMetadata>();
    }

    public async Task<List<ParameterMetadata>> GetProcedureParametersAsync(
        DbConnection connection, string schema, string procedureName, CancellationToken cancellationToken)
    {
        Log.Information("SQLite does not support stored procedures; skipping procedure metadata for {Procedure}", procedureName);
        return new List<ParameterMetadata>();
    }

    /// <summary>
    /// Gets column metadata for a SQLite table using PRAGMA table_info
    /// </summary>
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
            var dataType = reader["type"]?.ToString() ?? "TEXT";
            var col = new ColumnMetadata
            {
                DatabaseColumnName = reader["name"]?.ToString() ?? string.Empty,
                DataType = dataType,
                IsNullable = reader["notnull"] != DBNull.Value && Convert.ToInt32(reader["notnull"]) == 0,
                IsPrimaryKey = reader["pk"] != DBNull.Value && Convert.ToInt32(reader["pk"]) > 0
            };
            col.ClrType = MapSqlTypeToClr(dataType);
            columns.Add(col);
        }

        return columns;
    }
}
