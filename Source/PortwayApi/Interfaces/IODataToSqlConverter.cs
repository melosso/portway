namespace PortwayApi.Interfaces;

using DynamicODataToSQL;
using PortwayApi.Services.Providers;

/// <summary>
/// Interface for converting OData queries to SQL
/// </summary>
public interface IODataToSqlConverter
{
    /// <summary>
    /// Converts OData query parameters to SQL using the compiler for the specified provider
    /// </summary>
    (string SqlQuery, Dictionary<string, object> Parameters) ConvertToSQL(
        string entityName,
        Dictionary<string, string> odataParams,
        SqlProviderType providerType);

    /// <summary>
    /// Converts OData query parameters to SQL, emitting JOINs for the configured $expand navigations
    /// </summary>
    (string SqlQuery, Dictionary<string, object> Parameters) ConvertToSQL(
        string entityName,
        Dictionary<string, string> odataParams,
        SqlProviderType providerType,
        IReadOnlyList<PortwayApi.Classes.EndpointRelationship>? relationships);

    /// <summary>
    /// Converts OData query parameters to a COUNT query for the specified provider; only $filter applies
    /// </summary>
    (string SqlQuery, Dictionary<string, object> Parameters) ConvertToCountSQL(
        string entityName,
        Dictionary<string, string> odataParams,
        SqlProviderType providerType);

    /// <summary>
    /// Converts OData to SQL with tenant predicates ANDed after the grouped client conditions
    /// </summary>
    (string SqlQuery, Dictionary<string, object> Parameters) ConvertToSQL(
        string entityName,
        Dictionary<string, string> odataParams,
        SqlProviderType providerType,
        IReadOnlyList<PortwayApi.Classes.EndpointRelationship>? relationships,
        IReadOnlyList<PortwayApi.Helpers.TenantPredicate> tenants);

    /// <summary>
    /// Converts OData to a COUNT query with tenant predicates
    /// </summary>
    (string SqlQuery, Dictionary<string, object> Parameters) ConvertToCountSQL(
        string entityName,
        Dictionary<string, string> odataParams,
        SqlProviderType providerType,
        IReadOnlyList<PortwayApi.Helpers.TenantPredicate> tenants);
}
