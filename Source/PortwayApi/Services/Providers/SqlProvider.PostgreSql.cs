namespace PortwayApi.Services.Providers;

using System.Data.Common;
using Npgsql;
using PortwayApi.Services.Database;
using SqlKata.Compilers;

public class PostgreSqlProvider : SqlProviderBase
{
    public override SqlProviderType ProviderType => SqlProviderType.PostgreSql;
    public override bool SupportsTvf => true;
    public override string DefaultSchema => "public";

    public override DbConnection CreateConnection(string connectionString)
        => new NpgsqlConnection(connectionString);

    public override string OptimizeConnectionString(string connectionString, SqlPoolingOptions options)
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

    public override Compiler GetCompiler() => new PostgresCompiler();

    // Rowset-returning routines are functions here and CALL cannot return rows, so invoke via SELECT.
    // Named notation keeps argument order free; function parameter names are the lowercased payload keys
    public override (string CommandText, System.Data.CommandType CommandType) BuildProcedureInvocation(
        string schema, string procedureName, IReadOnlyCollection<string> parameterNames)
    {
        var arguments = string.Join(", ", parameterNames.Select(p => $"{p.ToLowerInvariant()} => @{p}"));
        return ($"SELECT * FROM \"{schema}\".\"{procedureName}\"({arguments})", System.Data.CommandType.Text);
    }

    public override string MapSqlTypeToClr(string nativeType) => nativeType.ToLowerInvariant() switch
    {
        "integer" or "int4" or "serial" => "System.Int32",
        "bigint" or "int8" or "bigserial" => "System.Int64",
        "smallint" or "int2" or "smallserial" => "System.Int16",
        "text" or "varchar" or "character varying" or "char" or "character" or "name" or "citext" => "System.String",
        "boolean" or "bool" => "System.Boolean",
        "uuid" => "System.Guid",
        "timestamp" or "timestamp without time zone" => "System.DateTime",
        // Timezone-aware types carry an offset, surface them as DateTimeOffset
        "timestamp with time zone" or "timestamptz" => "System.DateTimeOffset",
        "time with time zone" or "timetz" => "System.DateTimeOffset",
        "date" => "System.DateTime",
        "time" or "time without time zone" => "System.TimeSpan",
        "numeric" or "decimal" => "System.Decimal",
        "real" or "float4" => "System.Single",
        "double precision" or "float8" => "System.Double",
        "money" => "System.Decimal",
        "bytea" => "System.Byte[]",
        "json" or "jsonb" or "xml" => "System.String",
        "inet" or "cidr" or "macaddr" or "macaddr8" => "System.String",
        "interval" => "System.TimeSpan",
        _ => "System.Object"
    };

    public override async Task<List<ColumnMetadata>> GetTvfColumnsAsync(
        DbConnection connection, string schema, string functionName, CancellationToken cancellationToken)
    {
        // RETURNS TABLE and OUT columns live on the pg_proc argument arrays
        const string tableArgsQuery = """
            SELECT args.argname AS column_name, format_type(args.argtype, NULL) AS data_type
            FROM pg_proc p
            JOIN pg_namespace n ON n.oid = p.pronamespace
            CROSS JOIN LATERAL unnest(p.proallargtypes, p.proargmodes, p.proargnames)
                WITH ORDINALITY AS args(argtype, argmode, argname, ord)
            WHERE n.nspname = @schema AND p.proname = @function AND args.argmode IN ('o', 't')
            ORDER BY args.ord
            """;

        // RETURNS SETOF composite exposes columns through the return type's attributes
        const string compositeQuery = """
            SELECT a.attname AS column_name, format_type(a.atttypid, NULL) AS data_type, NOT a.attnotnull AS is_nullable
            FROM pg_proc p
            JOIN pg_namespace n ON n.oid = p.pronamespace
            JOIN pg_type t ON t.oid = p.prorettype
            JOIN pg_class c ON c.oid = t.typrelid
            JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
            WHERE n.nspname = @schema AND p.proname = @function
            ORDER BY a.attnum
            """;

        var columns = await QueryTvfColumnsAsync(connection, tableArgsQuery, schema, functionName, hasNullability: false, cancellationToken).ConfigureAwait(false);
        if (columns.Count == 0)
            columns = await QueryTvfColumnsAsync(connection, compositeQuery, schema, functionName, hasNullability: true, cancellationToken).ConfigureAwait(false);
        return columns;
    }

    private async Task<List<ColumnMetadata>> QueryTvfColumnsAsync(
        DbConnection connection, string query, string schema, string functionName, bool hasNullability, CancellationToken cancellationToken)
    {
        var columns = new List<ColumnMetadata>();
        using var command = CreateCommand(connection, query, ("@schema", schema), ("@function", functionName));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var dataType = ReadString(reader, "data_type");
            columns.Add(new ColumnMetadata
            {
                DatabaseColumnName = ReadString(reader, "column_name"),
                DataType = dataType,
                IsNullable = !hasNullability || ReadBool(reader, "is_nullable"),
                ClrType = MapSqlTypeToClr(dataType)
            });
        }
        return columns;
    }

    public override Task<List<ParameterMetadata>> GetProcedureParametersAsync(
        DbConnection connection, string schema, string procedureName, CancellationToken cancellationToken)
        => QueryInformationSchemaParametersAsync(connection, schema, procedureName, patternMatch: true, cancellationToken);
}
