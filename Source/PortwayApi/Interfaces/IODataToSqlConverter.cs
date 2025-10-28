namespace PortwayApi.Interfaces;

using Microsoft.OData.Edm;
using DynamicODataToSQL;

/// <summary>
/// Interface for converting OData queries to SQL
/// </summary>
public interface IODataToSqlConverter
{
    /// <summary>
    /// Converts OData query parameters to SQL with parameter values
    /// </summary>
    /// <param name="entityName">The entity/table name to query</param>
    /// <param name="odataParams">Dictionary of OData query parameters</param>
    /// <returns>Tuple with SQL query string and parameter dictionary</returns>
    (string SqlQuery, Dictionary<string, object> Parameters) ConvertToSQL(
        string entityName, 
        Dictionary<string, string> odataParams);
}

/// <summary>
/// Interface for building EDM models for OData queries
/// </summary>
public interface IEdmModelBuilder
{
    /// <summary>
    /// Gets an EDM model for the specified entity
    /// </summary>
    IEdmModel GetEdmModel(string entityName);
    
    /// <summary>
    /// Builds an EDM model for the specified table
    /// </summary>
    EdmModel BuildModel(string tableName);
    
    /// <summary>
    /// Parses an EDM model from CSDL XML
    /// </summary>
    IEdmModel? ParseMetadata(string csdl);
}