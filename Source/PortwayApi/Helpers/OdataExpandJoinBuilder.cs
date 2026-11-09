namespace PortwayApi.Helpers;

using PortwayApi.Classes;
using SqlKata;

/// <summary>
/// Adds to-one $expand navigations as SqlKata joins on the open base query
/// </summary>
public static class OdataExpandJoinBuilder
{
    public static Query Apply(Query query, string rootTable, IReadOnlyList<RelationalExpandSpec> specs)
    {
        foreach (var spec in specs)
        {
            // left join includes unmatched root rows
            query = query.LeftJoin($"{spec.TargetTable} as {spec.NavName}",
                j => j.On($"{spec.NavName}.{spec.TargetColumn}", $"{rootTable}.{spec.LocalColumn}"));

            // Namespaced alias so target columns arrive as dotted keys (Nav.Column) for later nesting
            foreach (var column in spec.TargetColumns.Distinct(StringComparer.OrdinalIgnoreCase))
                query = query.Select($"{spec.NavName}.{column} as {spec.NavName}.{column}");
        }

        return query;
    }
}
