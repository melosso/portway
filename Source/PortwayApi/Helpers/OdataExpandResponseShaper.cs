namespace PortwayApi.Helpers;

/// <summary>Nests the flat dotted keys an $expand JOIN produces (Nav.Column) into a Nav object per row.
/// Nested columns are mapped to the target endpoint's aliases, so an expanded entity reads the same as
/// it would from its own endpoint. Root keys pass through untouched</summary>
public static class OdataExpandResponseShaper
{
    public static List<Dictionary<string, object>> Nest(
        IEnumerable<object> rows,
        IReadOnlyList<(string NavName, IReadOnlyDictionary<string, string> DbToAlias)> navs)
    {
        var result = new List<Dictionary<string, object>>();

        foreach (var row in rows)
        {
            if (row is not IDictionary<string, object> dict)
            {
                // Not a reshapeable row; leave it as an empty object rather than throw
                result.Add(new Dictionary<string, object>());
                continue;
            }

            var outRow = new Dictionary<string, object>();
            var nested = navs.ToDictionary(
                n => n.NavName,
                _ => new Dictionary<string, object>(),
                StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in dict)
            {
                var match = navs.FirstOrDefault(n =>
                    kvp.Key.StartsWith(n.NavName + ".", StringComparison.OrdinalIgnoreCase));

                if (match.NavName != null)
                {
                    var dbColumn = kvp.Key[(match.NavName.Length + 1)..];
                    var key = match.DbToAlias.TryGetValue(dbColumn, out var alias) ? alias : dbColumn;
                    nested[match.NavName][key] = kvp.Value;
                }
                else
                {
                    outRow[kvp.Key] = kvp.Value;
                }
            }

            foreach (var nav in navs)
                outRow[nav.NavName] = nested[nav.NavName];

            result.Add(outRow);
        }

        return result;
    }
}
