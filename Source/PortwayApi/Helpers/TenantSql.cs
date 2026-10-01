using PortwayApi.Auth;
using PortwayApi.Classes;
using SqlKata;

namespace PortwayApi.Helpers;

/// <summary>
/// Tenant column and value
/// </summary>
public readonly record struct TenantPredicate(string Column, string Value);

/// <summary>
/// Tenant predicates and literals for SQL endpoints
/// </summary>
public static class TenantSql
{
    public static IReadOnlyList<TenantPredicate> Predicates(EndpointDefinition endpoint, IReadOnlyDictionary<string, string> tenants) =>
        endpoint.HasTenancy && tenants.Count > 0
            ? endpoint.Tenancy!.Select(t => new TenantPredicate(t.Value, tenants[t.Key])).ToList()
            : [];

    /// <summary>
    /// Quoted SQL literal for a tenant value; providers coerce it to the column type. Throws when the value fails the tenant grammar
    /// </summary>
    public static UnsafeLiteral Literal(string value) =>
        TenantGrants.IsValidValue(value)
            ? new UnsafeLiteral($"'{value}'", replaceQuotes: false)
            : throw new ArgumentException("Invalid tenant value", nameof(value));

    /// <summary>
    /// Wraps existing where clauses in one group and ANDs each tenant predicate
    /// </summary>
    public static Query Apply(Query query, string table, IReadOnlyList<TenantPredicate> predicates)
    {
        if (predicates.Count == 0)
            return query;

        var existing = query.GetComponents("where");
        if (existing.Count > 0)
        {
            query.ClearComponent("where");
            query.Where(group =>
            {
                foreach (var clause in existing)
                    group.AddComponent("where", clause);
                return group;
            });
        }

        foreach (var predicate in predicates)
            query.Where($"{table}.{predicate.Column}", Literal(predicate.Value));
        return query;
    }
}
