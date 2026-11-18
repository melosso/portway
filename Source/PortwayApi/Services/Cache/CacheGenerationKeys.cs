using System.Text.Json;
using System.Text.RegularExpressions;
using PortwayApi.Classes;

namespace PortwayApi.Services.Caching;

public static partial class CacheGenerationKeys
{
    public const string InvalidatedByProperty = "CacheInvalidatedBy";

    private const string DefaultSchema = "dbo";

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*(\\.[A-Za-z_][A-Za-z0-9_]*)?$")]
    private static partial Regex TableName();

    public static string SqlTable(string env, string? schema, string table) =>
        $"sql:generation:{env}:{schema ?? DefaultSchema}.{table}".ToLowerInvariant();

    public static string ProxyEndpoint(string env, string endpointName) =>
        $"proxy:generation:{env}:{endpointName}".ToLowerInvariant();

    public static IReadOnlyList<string> SqlEndpoint(string env, EndpointDefinition endpoint)
    {
        var keys = new List<string> { SqlTable(env, endpoint.DatabaseSchema, endpoint.DatabaseObjectName ?? string.Empty) };
        foreach (var entry in InvalidatedBy(endpoint.Properties))
        {
            var dot = entry.IndexOf('.');
            keys.Add(dot < 0
                ? SqlTable(env, endpoint.DatabaseSchema, entry)
                : SqlTable(env, entry[..dot], entry[(dot + 1)..]));
        }
        return keys;
    }

    public static IEnumerable<string> Validate(IReadOnlyDictionary<string, object>? properties)
    {
        if (properties?.TryGetValue(InvalidatedByProperty, out var value) != true)
            yield break;

        if (value is not JsonElement { ValueKind: JsonValueKind.Array } array)
        {
            yield return $"{InvalidatedByProperty} must be an array of table names";
            yield break;
        }

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || !TableName().IsMatch(item.GetString()!))
                yield return $"{InvalidatedByProperty} entry '{item}' is not a table name (Table or Schema.Table)";
        }
    }

    private static IEnumerable<string> InvalidatedBy(IReadOnlyDictionary<string, object>? properties) =>
        properties?.TryGetValue(InvalidatedByProperty, out var value) == true && value is JsonElement { ValueKind: JsonValueKind.Array } array
            ? array.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.String).Select(i => i.GetString()!).Where(name => TableName().IsMatch(name))
            : [];
}
