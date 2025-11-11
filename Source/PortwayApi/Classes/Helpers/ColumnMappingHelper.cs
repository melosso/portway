using Serilog;
using System.Text.RegularExpressions;

namespace PortwayApi.Classes.Helpers;

/// <summary>
/// Helper class for handling column alias mappings in SQL endpoints
/// </summary>
public static class ColumnMappingHelper
{
    /// <summary>
    /// Parses semicolon-separated column mappings and returns dictionaries for both directions
    /// Format: "DatabaseColumn;Alias" or just "DatabaseColumn" (falls back to same name)
    /// </summary>
    /// <param name="allowedColumns">List of column definitions with optional semicolon aliases</param>
    /// <returns>Tuple with (AliasToDatabase, DatabaseToAlias) dictionaries</returns>
    public static (Dictionary<string, string> AliasToDatabase, Dictionary<string, string> DatabaseToAlias) 
        ParseColumnMappings(List<string>? allowedColumns)
    {
        var aliasToDatabase = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var databaseToAlias = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (allowedColumns == null || allowedColumns.Count == 0)
        {
            Log.Debug("No allowed columns provided for mapping");
            return (aliasToDatabase, databaseToAlias);
        }

        foreach (var columnDef in allowedColumns)
        {
            if (string.IsNullOrWhiteSpace(columnDef))
            {
                Log.Warning("Skipping empty column definition");
                continue;
            }

            var parts = columnDef.Split(';', StringSplitOptions.RemoveEmptyEntries);
            
            if (parts.Length == 0)
            {
                // Handle the case where columnDef is just ";" or only contains separators
                Log.Warning("Skipping invalid column definition '{ColumnDef}' - contains only separators", columnDef);
                continue;
            }
            else if (parts.Length == 1)
            {
                // No semicolon found - use the column name as both database and alias
                var columnName = parts[0].Trim();
                if (!string.IsNullOrWhiteSpace(columnName))
                {
                    aliasToDatabase[columnName] = columnName;
                    databaseToAlias[columnName] = columnName;
                    Log.Debug("Column mapping: {ColumnName} -> {ColumnName} (no alias)", columnName, columnName);
                }
            }
            else if (parts.Length == 2)
            {
                // Semicolon found - map database column to alias
                var databaseColumn = parts[0].Trim();
                var alias = parts[1].Trim();
                
                if (!string.IsNullOrWhiteSpace(databaseColumn) && !string.IsNullOrWhiteSpace(alias))
                {
                    aliasToDatabase[alias] = databaseColumn;
                    databaseToAlias[databaseColumn] = alias;
                    Log.Debug("Column mapping: {Alias} -> {DatabaseColumn}", alias, databaseColumn);
                }
                else
                {
                    // Fallback: if either part is empty, use the non-empty part for both
                    var columnName = !string.IsNullOrWhiteSpace(databaseColumn) ? databaseColumn : alias;
                    if (!string.IsNullOrWhiteSpace(columnName))
                    {
                        aliasToDatabase[columnName] = columnName;
                        databaseToAlias[columnName] = columnName;
                        Log.Warning("Malformed column mapping '{ColumnDef}', falling back to: {ColumnName} -> {ColumnName}", 
                            columnDef, columnName, columnName);
                    }
                }
            }
            else
            {
                // More than one semicolon - malformed, fallback to using the first part
                var columnName = parts[0].Trim();
                if (!string.IsNullOrWhiteSpace(columnName))
                {
                    aliasToDatabase[columnName] = columnName;
                    databaseToAlias[columnName] = columnName;
                    Log.Warning("Malformed column mapping '{ColumnDef}' (multiple semicolons), falling back to: {ColumnName} -> {ColumnName}", 
                        columnDef, columnName, columnName);
                }
            }
        }

        Log.Debug("Parsed {Count} column mappings", aliasToDatabase.Count);
        return (aliasToDatabase, databaseToAlias);
    }

    /// <summary>
    /// Converts alias column names to database column names for SQL queries
    /// </summary>
    /// <param name="aliasColumns">Comma-separated list of alias column names</param>
    /// <param name="aliasToDatabase">Mapping from alias to database column names</param>
    /// <returns>Comma-separated list of database column names</returns>
    public static string ConvertAliasesToDatabaseColumns(string? aliasColumns, Dictionary<string, string> aliasToDatabase)
    {
        if (string.IsNullOrWhiteSpace(aliasColumns) || aliasToDatabase.Count == 0)
        {
            return aliasColumns ?? string.Empty;
        }

        var aliases = aliasColumns.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(alias => alias.Trim())
            .Where(alias => !string.IsNullOrWhiteSpace(alias));

        var databaseColumns = new List<string>();
        
        foreach (var alias in aliases)
        {
            if (aliasToDatabase.TryGetValue(alias, out var databaseColumn))
            {
                databaseColumns.Add(databaseColumn);
                Log.Debug("Converted alias '{Alias}' to database column '{DatabaseColumn}'", alias, databaseColumn);
            }
            else
            {
                // If no mapping found, use the alias as-is (fallback behavior)
                databaseColumns.Add(alias);
                Log.Debug("No mapping found for alias '{Alias}', using as-is", alias);
            }
        }

        return string.Join(",", databaseColumns);
    }

    /// <summary>
    /// Converts database column names to alias column names for API responses
    /// </summary>
    /// <param name="databaseColumns">List of database column names</param>
    /// <param name="databaseToAlias">Mapping from database to alias column names</param>
    /// <returns>List of alias column names</returns>
    public static List<string> ConvertDatabaseColumnsToAliases(List<string> databaseColumns, Dictionary<string, string> databaseToAlias)
    {
        if (databaseColumns == null || databaseColumns.Count == 0 || databaseToAlias.Count == 0)
        {
            return databaseColumns ?? new List<string>();
        }

        var aliasColumns = new List<string>();
        
        foreach (var databaseColumn in databaseColumns)
        {
            if (databaseToAlias.TryGetValue(databaseColumn, out var alias))
            {
                aliasColumns.Add(alias);
                Log.Debug("Converted database column '{DatabaseColumn}' to alias '{Alias}'", databaseColumn, alias);
            }
            else
            {
                // If no mapping found, use the database column as-is (fallback behavior)
                aliasColumns.Add(databaseColumn);
                Log.Debug("No mapping found for database column '{DatabaseColumn}', using as-is", databaseColumn);
            }
        }

        return aliasColumns;
    }

    /// <summary>
    /// Validates that all requested alias columns are allowed
    /// </summary>
    /// <param name="requestedAliases">Comma-separated list of requested alias column names</param>
    /// <param name="aliasToDatabase">Mapping from alias to database column names</param>
    /// <returns>Tuple with (IsValid, InvalidAliases)</returns>
    public static (bool IsValid, List<string> InvalidAliases) ValidateAliasColumns(
        string? requestedAliases, 
        Dictionary<string, string> aliasToDatabase)
    {
        if (string.IsNullOrWhiteSpace(requestedAliases))
        {
            return (true, new List<string>());
        }

        var aliases = requestedAliases.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(alias => alias.Trim())
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .ToList();

        var invalidAliases = new List<string>();

        foreach (var alias in aliases)
        {
            if (!aliasToDatabase.ContainsKey(alias))
            {
                invalidAliases.Add(alias);
            }
        }

        bool isValid = invalidAliases.Count == 0;
        
        if (!isValid)
        {
            Log.Warning("Invalid alias columns requested: {InvalidAliases}", string.Join(", ", invalidAliases));
        }

        return (isValid, invalidAliases);
    }

    /// <summary>
    /// Gets all allowed alias column names
    /// </summary>
    /// <param name="aliasToDatabase">Mapping from alias to database column names</param>
    /// <returns>List of all allowed alias column names</returns>
    public static List<string> GetAllowedAliases(Dictionary<string, string> aliasToDatabase)
    {
        return aliasToDatabase.Keys.ToList();
    }

    /// <summary>
    /// Gets all database column names
    /// </summary>
    /// <param name="databaseToAlias">Mapping from database to alias column names</param>
    /// <returns>List of all database column names</returns>
    public static List<string> GetDatabaseColumns(Dictionary<string, string> databaseToAlias)
    {
        return databaseToAlias.Keys.ToList();
    }

    /// <summary>
    /// Converts alias column references in OData filter expressions to database column names
    /// </summary>
    /// <param name="filterExpression">OData filter expression that may contain alias column names</param>
    /// <param name="aliasToDatabase">Mapping from alias to database column names</param>
    /// <returns>Filter expression with database column names</returns>
    public static string ConvertODataFilterAliases(string filterExpression, Dictionary<string, string> aliasToDatabase)
    {
        if (string.IsNullOrWhiteSpace(filterExpression) || aliasToDatabase.Count == 0)
        {
            return filterExpression;
        }

        var convertedFilter = filterExpression;
        
        // Sort aliases by length (descending) to avoid partial replacements
        var sortedAliases = aliasToDatabase.Keys.OrderByDescending(alias => alias.Length);
        
        foreach (var alias in sortedAliases)
        {
            if (aliasToDatabase.TryGetValue(alias, out var databaseColumn))
            {
                // Use word boundary regex to avoid partial matches
                // This will match the alias when it's a separate word (not part of another word)
                var pattern = $@"\b{Regex.Escape(alias)}\b";
                convertedFilter = Regex.Replace(convertedFilter, pattern, databaseColumn, RegexOptions.IgnoreCase);
            }
        }
        
        return convertedFilter;
    }

    /// <summary>
    /// Converts alias column references in OData orderby expressions to database column names
    /// </summary>
    /// <param name="orderByExpression">OData orderby expression that may contain alias column names</param>
    /// <param name="aliasToDatabase">Mapping from alias to database column names</param>
    /// <returns>OrderBy expression with database column names</returns>
    public static string ConvertODataOrderByAliases(string orderByExpression, Dictionary<string, string> aliasToDatabase)
    {
        if (string.IsNullOrWhiteSpace(orderByExpression) || aliasToDatabase.Count == 0)
        {
            return orderByExpression;
        }

        var convertedOrderBy = orderByExpression;
        
        // Sort aliases by length (descending) to avoid partial replacements
        var sortedAliases = aliasToDatabase.Keys.OrderByDescending(alias => alias.Length);
        
        foreach (var alias in sortedAliases)
        {
            if (aliasToDatabase.TryGetValue(alias, out var databaseColumn))
            {
                // Use word boundary regex to avoid partial matches
                // This will match the alias when it's a separate word (not part of another word)
                var pattern = $@"\b{Regex.Escape(alias)}\b";
                convertedOrderBy = Regex.Replace(convertedOrderBy, pattern, databaseColumn, RegexOptions.IgnoreCase);
            }
        }
        
        return convertedOrderBy;
    }

    /// <summary>
    /// Transforms query results by converting database column names to aliases in the response
    /// </summary>
    /// <param name="results">Raw query results with database column names</param>
    /// <param name="databaseToAlias">Mapping from database to alias column names</param>
    /// <returns>Transformed results with alias column names</returns>
    public static List<Dictionary<string, object>> TransformQueryResultsToAliases(
        IEnumerable<object> results, 
        Dictionary<string, string> databaseToAlias)
    {
        if (results == null || databaseToAlias.Count == 0)
        {
            // If no mapping needed, convert to dictionary format
            return results?.Select(r => ConvertToDictionary(r)).ToList() ?? new List<Dictionary<string, object>>();
        }

        var transformedResults = new List<Dictionary<string, object>>();

        foreach (var result in results)
        {
            var originalDict = ConvertToDictionary(result);
            var transformedDict = new Dictionary<string, object>();

            foreach (var kvp in originalDict)
            {
                var databaseColumn = kvp.Key;
                var value = kvp.Value;

                // Convert database column name to alias if mapping exists
                if (databaseToAlias.TryGetValue(databaseColumn, out var alias))
                {
                    transformedDict[alias] = value;
                    Log.Debug("Transformed result column: '{DatabaseColumn}' -> '{Alias}'", databaseColumn, alias);
                }
                else
                {
                    // If no mapping found, use the database column name as-is
                    transformedDict[databaseColumn] = value;
                    Log.Debug("No alias mapping for column '{DatabaseColumn}', using as-is", databaseColumn);
                }
            }

            transformedResults.Add(transformedDict);
        }

        Log.Debug("Transformed {Count} result records from database columns to aliases", transformedResults.Count);
        return transformedResults;
    }

    /// <summary>
    /// Converts an object to a dictionary representation
    /// </summary>
    /// <param name="obj">Object to convert (typically a Dapper result)</param>
    /// <returns>Dictionary representation of the object</returns>
    private static Dictionary<string, object> ConvertToDictionary(object obj)
    {
        if (obj == null)
        {
            return new Dictionary<string, object>();
        }

        // Handle Dapper DynamicRow objects and other dictionary-like objects
        if (obj is IDictionary<string, object> dict)
        {
            return new Dictionary<string, object>(dict);
        }

        // Handle regular objects using reflection
        var result = new Dictionary<string, object>();
        var properties = obj.GetType().GetProperties();

        foreach (var property in properties)
        {
            try
            {
                var value = property.GetValue(obj);
                result[property.Name] = value ?? DBNull.Value;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to get value for property '{PropertyName}'", property.Name);
                result[property.Name] = DBNull.Value;
            }
        }

        return result;
    }
}
