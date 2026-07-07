namespace PortwayApi.Interfaces;

using Microsoft.OData.Edm;
using DynamicODataToSQL;
using PortwayApi.Classes.Providers;

/// <summary>Interface for converting OData queries to SQL</summary>
public interface IODataToSqlConverter
{
    /// <summary>Converts OData query parameters to SQL (defaults to SqlServer compiler)</summary>
    (string SqlQuery, Dictionary<string, object> Parameters) ConvertToSQL(
        string entityName,
        Dictionary<string, string> odataParams);

    /// <summary>Converts OData query parameters to SQL using the compiler for the specified provider</summary>
    (string SqlQuery, Dictionary<string, object> Parameters) ConvertToSQL(
        string entityName,
        Dictionary<string, string> odataParams,
        SqlProviderType providerType);

    /// <summary>Converts OData query parameters to a COUNT query for the specified provider; only $filter applies</summary>
    (string SqlQuery, Dictionary<string, object> Parameters) ConvertToCountSQL(
        string entityName,
        Dictionary<string, string> odataParams,
        SqlProviderType providerType);
}
