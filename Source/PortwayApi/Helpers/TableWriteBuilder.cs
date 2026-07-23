namespace PortwayApi.Helpers;

using System.Text.RegularExpressions;
using PortwayApi.Classes;
using PortwayApi.Services.Providers;
using SqlKata;

/// <summary>One compiled statement with its bound parameters</summary>
public sealed record TableWriteCommand(string Sql, Dictionary<string, object?> Parameters);

/// <summary>Builds guarded INSERT/UPDATE/DELETE statements for WriteMode: Table endpoints.
/// Every identifier comes from validated endpoint configuration, every value is a bound parameter,
/// and every predicate is primary key equality only</summary>
public static partial class TableWriteBuilder
{
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex SafeIdentifier();

    /// <summary>Returns null when the endpoint is safely configured for table writes, an error otherwise</summary>
    public static string? ValidateConfig(EndpointDefinition endpoint)
    {
        if (!endpoint.UsesTableWrites)
            return "Endpoint is not configured for table writes";
        if (!string.IsNullOrEmpty(endpoint.Procedure))
            return "WriteMode Table and Procedure are mutually exclusive";
        if (!string.Equals(endpoint.DatabaseObjectType, "Table", StringComparison.OrdinalIgnoreCase))
            return "Table writes require DatabaseObjectType Table";
        if (endpoint.AllowedColumns is not { Count: > 0 })
            return "Table writes require an AllowedColumns allowlist";
        if (string.IsNullOrWhiteSpace(endpoint.PrimaryKey))
            return "Table writes require a PrimaryKey column";
        if (string.IsNullOrWhiteSpace(endpoint.DatabaseObjectName) || !SafeIdentifier().IsMatch(endpoint.DatabaseObjectName))
            return "Table writes require a plain DatabaseObjectName identifier";
        if (endpoint.DatabaseSchema is { Length: > 0 } schema && !SafeIdentifier().IsMatch(schema))
            return "DatabaseSchema is not a plain identifier";
        if (!SafeIdentifier().IsMatch(ResolvePrimaryKeyColumn(endpoint)))
            return "PrimaryKey is not a plain identifier";

        foreach (var column in endpoint.AliasToDatabase.Values)
            if (!SafeIdentifier().IsMatch(column))
                return $"AllowedColumns contains an unsafe column name: {column}";

        return null;
    }

    /// <summary>Maps payload aliases to database columns, rejecting anything outside the allowlist</summary>
    public static bool TryResolveColumns(
        EndpointDefinition endpoint,
        IReadOnlyDictionary<string, object?> payload,
        out Dictionary<string, object?> columns,
        out string? error)
    {
        columns = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var aliasToDb = endpoint.AliasToDatabase;

        foreach (var (alias, value) in payload)
        {
            // Fail closed: unknown fields are rejected, never silently dropped
            if (!aliasToDb.TryGetValue(alias, out var dbColumn))
            {
                error = $"Column '{alias}' is not allowed on this endpoint";
                return false;
            }
            columns[dbColumn] = value;
        }

        if (columns.Count == 0)
        {
            error = "Request contains no writable columns";
            return false;
        }

        error = null;
        return true;
    }

    public static string ResolvePrimaryKeyColumn(EndpointDefinition endpoint)
        => endpoint.AliasToDatabase.TryGetValue(endpoint.PrimaryKey ?? "", out var db) ? db : endpoint.PrimaryKey ?? "";

    public static TableWriteCommand BuildInsert(ISqlProvider provider, string table, Dictionary<string, object?> columns)
        => Compile(provider, new Query(table).AsInsert(columns));

    public static TableWriteCommand BuildUpdate(ISqlProvider provider, string table, string pkColumn, object pkValue, Dictionary<string, object?> columns)
        => Compile(provider, new Query(table).Where(pkColumn, pkValue).AsUpdate(columns));

    public static TableWriteCommand BuildDelete(ISqlProvider provider, string table, string pkColumn, object pkValue)
        => Compile(provider, new Query(table).Where(pkColumn, pkValue).AsDelete());

    public static TableWriteCommand BuildSelectByKey(ISqlProvider provider, string table, string pkColumn, object pkValue)
        => Compile(provider, new Query(table).Where(pkColumn, pkValue));

    private static TableWriteCommand Compile(ISqlProvider provider, Query query)
    {
        var compiled = provider.GetCompiler().Compile(query);
        return new TableWriteCommand(
            compiled.Sql,
            new Dictionary<string, object?>(compiled.NamedBindings));
    }
}
