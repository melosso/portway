namespace PortwayApi.Helpers;

using PortwayApi.Classes;
using SqlKata;

/// <summary>Adds to-one $expand navigations as SqlKata JOINs onto a base query built by the fork.
/// The base query stays on the fork's open model so root filters are never type-checked; the JOIN is
/// added here. Identifiers are plain-identifier validated at config, SqlKata quotes them per dialect</summary>
public static class OdataExpandJoinBuilder
{
    public static Query Apply(Query query, string rootTable, IReadOnlyList<RelationalExpandSpec> specs)
    {
        foreach (var spec in specs)
        {
            // INNER JOIN target AS Nav ON Nav.TargetColumn = root.LocalColumn (to-one)
            query = query.Join($"{spec.TargetTable} as {spec.NavName}",
                j => j.On($"{spec.NavName}.{spec.TargetColumn}", $"{rootTable}.{spec.LocalColumn}"),
                "inner join");

            // Namespaced alias so target columns arrive as dotted keys (Nav.Column) for later nesting
            foreach (var column in spec.TargetColumns.Distinct(StringComparer.OrdinalIgnoreCase))
                query = query.Select($"{spec.NavName}.{column} as {spec.NavName}.{column}");
        }

        return query;
    }
}
