namespace PortwayApi.Helpers;

using System.Text.RegularExpressions;
using PortwayApi.Classes;

/// <summary>Shape validation for $expand relationship config. Fail closed: identifiers must be plain,
/// only to-one is expressible (fork JoinClauseBuilder constraint), and TVF endpoints cannot carry
/// relationships because their hybrid splice path drops JOINs. Target resolution is a separate
/// cross-endpoint check that runs where the full SQL endpoint set is known</summary>
public static partial class OdataExpandRelationshipValidator
{
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex Identifier();

    // Target may be a namespaced endpoint key (e.g. "Product/Assortments"); one optional segment
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*(/[A-Za-z_][A-Za-z0-9_]*)?$")]
    private static partial Regex EndpointRef();

    /// <summary>Returns config shape errors for the entity's relationships; empty when valid or none declared</summary>
    public static List<string> ValidateShape(EndpointEntity entity)
    {
        var errors = new List<string>();
        if (entity.Relationships is not { Count: > 0 })
            return errors;

        // BLOCKER #2: a TVF cannot JOIN, so a relationship on one would silently drop the expand
        if (!string.IsNullOrEmpty(entity.DatabaseObjectType) &&
            entity.DatabaseObjectType.Equals("TableValuedFunction", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Relationships ($expand) are not supported on TableValuedFunction endpoints");
            return errors;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < entity.Relationships.Count; i++)
        {
            var rel = entity.Relationships[i];

            if (string.IsNullOrWhiteSpace(rel.Name) || !Identifier().IsMatch(rel.Name))
                errors.Add($"Relationships[{i}] Name '{rel.Name}' is not a plain identifier");
            else if (!seen.Add(rel.Name))
                errors.Add($"Relationships[{i}] duplicate Name '{rel.Name}'");

            if (string.IsNullOrWhiteSpace(rel.Target) || !EndpointRef().IsMatch(rel.Target))
                errors.Add($"Relationships[{i}] Target '{rel.Target}' is not a valid endpoint reference");

            if (string.IsNullOrWhiteSpace(rel.LocalColumn) || !Identifier().IsMatch(rel.LocalColumn))
                errors.Add($"Relationships[{i}] LocalColumn '{rel.LocalColumn}' is not a plain identifier");

            if (string.IsNullOrWhiteSpace(rel.TargetColumn) || !Identifier().IsMatch(rel.TargetColumn))
                errors.Add($"Relationships[{i}] TargetColumn '{rel.TargetColumn}' is not a plain identifier");

            // BLOCKER #3: fork is to-one only; reject to-many at parse rather than emit wrong SQL
            if (!string.IsNullOrEmpty(rel.Multiplicity) &&
                !rel.Multiplicity.Equals("ToOne", StringComparison.OrdinalIgnoreCase))
                errors.Add($"Relationships[{i}] Multiplicity '{rel.Multiplicity}' is unsupported; only ToOne is allowed");
        }

        return errors;
    }
}
