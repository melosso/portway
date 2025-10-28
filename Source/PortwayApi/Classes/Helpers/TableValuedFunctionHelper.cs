using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Serilog;
using PortwayApi.Classes;

namespace PortwayApi.Classes.Helpers;

/// <summary>
/// Helper class for handling Table Valued Function (TVF) parameter extraction and SQL generation
/// </summary>
public static class TableValuedFunctionHelper
{
    /// <summary>
    /// Determines if an endpoint is a Table Valued Function
    /// </summary>
    /// <param name="endpoint">Endpoint definition</param>
    /// <returns>True if it's a TVF endpoint</returns>
    public static bool IsTableValuedFunction(EndpointDefinition endpoint)
    {
        return !string.IsNullOrEmpty(endpoint.DatabaseObjectType) &&
               endpoint.DatabaseObjectType.Equals("TableValuedFunction", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Extracts parameter values from HTTP request based on TVF parameter configuration
    /// </summary>
    /// <param name="functionParameters">TVF parameter definitions</param>
    /// <param name="request">HTTP request</param>
    /// <param name="pathSegments">URL path segments after the endpoint name</param>
    /// <returns>Dictionary of parameter values</returns>
    public static (Dictionary<string, object> Parameters, List<string> Errors) ExtractParameterValues(
        List<TVFParameter> functionParameters,
        HttpRequest request,
        string[] pathSegments)
    {
        var parameters = new Dictionary<string, object>();
        var errors = new List<string>();

        foreach (var param in functionParameters)
        {
            object? value = null;
            bool found = false;

            try
            {
                switch (param.Source.ToLower())
                {
                    case "path":
                        value = ExtractPathParameter(param, pathSegments);
                        found = value != null;
                        break;

                    case "query":
                        value = ExtractQueryParameter(param, request.Query);
                        found = value != null;
                        break;

                    case "header":
                        value = ExtractHeaderParameter(param, request.Headers);
                        found = value != null;
                        break;

                    default:
                        errors.Add($"Invalid parameter source '{param.Source}' for parameter '{param.Name}'");
                        continue;
                }

                // Handle required parameters
                if (!found && param.Required && string.IsNullOrEmpty(param.DefaultValue))
                {
                    errors.Add($"Required parameter '{param.Name}' not provided");
                    continue;
                }

                // Use default value if parameter not found and default is specified
                if (!found && !string.IsNullOrEmpty(param.DefaultValue))
                {
                    // Don't add SQL keywords like "DEFAULT" to parameters dictionary
                    // They will be handled directly in BuildFunctionCall
                    if (!param.DefaultValue.Equals("DEFAULT", StringComparison.OrdinalIgnoreCase))
                    {
                        parameters[param.Name] = param.DefaultValue;
                        Log.Debug("Using default value for parameter '{Parameter}': {DefaultValue}", param.Name, param.DefaultValue);
                    }
                    else
                    {
                        Log.Debug("Parameter '{Parameter}' will use SQL DEFAULT keyword", param.Name);
                    }
                }
                else if (found && value != null)
                {
                    // Validate the parameter value
                    var (isValid, validationError) = ValidateParameterValue(param, value.ToString()!);
                    if (!isValid)
                    {
                        errors.Add(validationError!);
                        continue;
                    }

                    parameters[param.Name] = value;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error extracting parameter '{Parameter}' from {Source}", param.Name, param.Source);
                errors.Add($"Error processing parameter '{param.Name}': {ex.Message}");
            }
        }

        return (parameters, errors);
    }

    /// <summary>
    /// Builds the SQL function call with parameters
    /// </summary>
    /// <param name="schema">Database schema</param>
    /// <param name="functionName">Function name</param>
    /// <param name="parameterValues">Parameter values</param>
    /// <param name="functionParameters">Function parameter definitions</param>
    /// <returns>SQL function call and parameters for Dapper</returns>
    public static (string FunctionCall, Dictionary<string, object> SqlParameters) BuildFunctionCall(
        string schema,
        string functionName,
        Dictionary<string, object> parameterValues,
        List<TVFParameter> functionParameters)
    {
        var sqlBuilder = new StringBuilder();
        var sqlParameters = new Dictionary<string, object>();

        sqlBuilder.Append($"SELECT * FROM [{schema}].[{functionName}](");

        var paramParts = new List<string>();
        for (int i = 0; i < functionParameters.Count; i++)
        {
            var param = functionParameters[i];
            var paramKey = $"@param{i}";

            if (parameterValues.TryGetValue(param.Name, out var value))
            {
                // Use parameterized query for security
                paramParts.Add($"{paramKey}");
                sqlParameters[paramKey] = ConvertToSqlType(value, param.SqlType);
            }
            else if (!string.IsNullOrEmpty(param.DefaultValue))
            {
                // Use default value directly in SQL (should be a SQL expression)
                paramParts.Add(param.DefaultValue);
            }
            else
            {
                // This shouldn't happen if validation was done correctly
                paramParts.Add("NULL");
            }
        }

        sqlBuilder.Append(string.Join(", ", paramParts));
        sqlBuilder.Append(")");

        Log.Debug("Built TVF call: {FunctionCall}", sqlBuilder.ToString());
        Log.Debug("SQL Parameters: {Parameters}", string.Join(", ", sqlParameters.Select(p => $"{p.Key}={p.Value}")));

        return (sqlBuilder.ToString(), sqlParameters);
    }

    /// <summary>
    /// Extracts a parameter value from the URL path
    /// </summary>
    private static object? ExtractPathParameter(TVFParameter param, string[] pathSegments)
    {
        if (param.Position <= 0 || param.Position > pathSegments.Length)
        {
            return null;
        }

        // Position is 1-based
        var segment = pathSegments[param.Position - 1];
        return string.IsNullOrEmpty(segment) ? null : segment;
    }

    /// <summary>
    /// Extracts a parameter value from query parameters
    /// </summary>
    private static object? ExtractQueryParameter(TVFParameter param, IQueryCollection query)
    {
        var queryKey = param.QueryParameterName ?? param.Name;
        
        if (query.TryGetValue(queryKey, out var values) && values.Count > 0)
        {
            var value = values.First();
            return string.IsNullOrEmpty(value) ? null : value;
        }

        return null;
    }

    /// <summary>
    /// Extracts a parameter value from request headers
    /// </summary>
    private static object? ExtractHeaderParameter(TVFParameter param, IHeaderDictionary headers)
    {
        var headerKey = param.HeaderName ?? param.Name;
        
        if (headers.TryGetValue(headerKey, out var values) && values.Count > 0)
        {
            var value = values.First();
            return string.IsNullOrEmpty(value) ? null : value;
        }

        return null;
    }

    /// <summary>
    /// Validates a parameter value against its configuration
    /// </summary>
    private static (bool IsValid, string? Error) ValidateParameterValue(TVFParameter param, string value)
    {
        // Check validation pattern if specified
        if (!string.IsNullOrEmpty(param.ValidationPattern))
        {
            try
            {
                if (!Regex.IsMatch(value, param.ValidationPattern))
                {
                    return (false, $"Parameter '{param.Name}' does not match required pattern");
                }
            }
            catch (Exception ex)
            {
                Log.Warning("Invalid regex pattern for parameter '{Parameter}': {Pattern}. Error: {Error}", 
                    param.Name, param.ValidationPattern, ex.Message);
            }
        }

        // Basic type validation
        return ValidateSqlType(value, param.SqlType);
    }

    /// <summary>
    /// Validates that a value can be converted to the specified SQL type
    /// </summary>
    private static (bool IsValid, string? Error) ValidateSqlType(string value, string sqlType)
    {
        var normalizedType = sqlType.ToUpper();

        try
        {
            if (normalizedType.StartsWith("INT") || normalizedType.StartsWith("BIGINT"))
            {
                if (!long.TryParse(value, out _))
                {
                    return (false, $"Value '{value}' is not a valid integer");
                }
            }
            else if (normalizedType.StartsWith("DECIMAL") || normalizedType.StartsWith("FLOAT") || normalizedType.StartsWith("REAL"))
            {
                if (!decimal.TryParse(value, out _))
                {
                    return (false, $"Value '{value}' is not a valid decimal number");
                }
            }
            else if (normalizedType.StartsWith("DATETIME") || normalizedType.StartsWith("DATE"))
            {
                if (!DateTime.TryParse(value, out _))
                {
                    return (false, $"Value '{value}' is not a valid date/time");
                }
            }
            else if (normalizedType.StartsWith("UNIQUEIDENTIFIER"))
            {
                if (!Guid.TryParse(value, out _))
                {
                    return (false, $"Value '{value}' is not a valid GUID");
                }
            }
            else if (normalizedType.StartsWith("BIT"))
            {
                var lowerValue = value.ToLower();
                if (lowerValue != "true" && lowerValue != "false" && lowerValue != "1" && lowerValue != "0")
                {
                    return (false, $"Value '{value}' is not a valid boolean (true/false or 1/0)");
                }
            }
            // NVARCHAR, VARCHAR, CHAR - accept any string value

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Error validating value '{value}' for SQL type '{sqlType}': {ex.Message}");
        }
    }

    /// <summary>
    /// Converts a string value to the appropriate .NET type for SQL parameters
    /// </summary>
    private static object ConvertToSqlType(object value, string sqlType)
    {
        var stringValue = value.ToString()!;
        var normalizedType = sqlType.ToUpper();

        try
        {
            if (normalizedType.StartsWith("INT"))
            {
                return int.Parse(stringValue);
            }
            else if (normalizedType.StartsWith("BIGINT"))
            {
                return long.Parse(stringValue);
            }
            else if (normalizedType.StartsWith("DECIMAL") || normalizedType.StartsWith("FLOAT") || normalizedType.StartsWith("REAL"))
            {
                return decimal.Parse(stringValue);
            }
            else if (normalizedType.StartsWith("DATETIME") || normalizedType.StartsWith("DATE"))
            {
                return DateTime.Parse(stringValue);
            }
            else if (normalizedType.StartsWith("UNIQUEIDENTIFIER"))
            {
                return Guid.Parse(stringValue);
            }
            else if (normalizedType.StartsWith("BIT"))
            {
                var lowerValue = stringValue.ToLower();
                return lowerValue == "true" || lowerValue == "1";
            }
            else
            {
                // Default to string for NVARCHAR, VARCHAR, CHAR, etc.
                return stringValue;
            }
        }
        catch (Exception ex)
        {
            Log.Warning("Failed to convert '{Value}' to SQL type '{SqlType}': {Error}. Using string value.", 
                stringValue, sqlType, ex.Message);
            return stringValue;
        }
    }

    // Removed ApplyODataToTVF: now handled securely in TableValuedFunctionSqlHandler using IODataToSqlConverter

    /// <summary>
    /// Converts OData filter syntax to SQL WHERE clause (simplified)
    /// </summary>
    private static string ConvertODataFilterToSQL(string filter, Dictionary<string, string>? aliasToDb = null)
    {
        // This is a very basic conversion. For production use, consider using
        // a proper OData parser or the existing DynamicODataToSQL library
        var sqlFilter = filter
            .Replace(" eq ", " = ")
            .Replace(" ne ", " <> ")
            .Replace(" lt ", " < ")
            .Replace(" le ", " <= ")
            .Replace(" gt ", " > ")
            .Replace(" ge ", " >= ")
            .Replace(" and ", " AND ")
            .Replace(" or ", " OR ");

        // If we have column mappings, try to translate aliases to database column names
        if (aliasToDb != null && aliasToDb.Count > 0)
        {
            foreach (var mapping in aliasToDb)
            {
                // Replace alias with database column name (case-insensitive)
                sqlFilter = Regex.Replace(sqlFilter, $@"\b{Regex.Escape(mapping.Key)}\b", mapping.Value, RegexOptions.IgnoreCase);
            }
        }

        return sqlFilter;
    }

    /// <summary>
    /// Converts OData orderby syntax to SQL ORDER BY clause (simplified)
    /// </summary>
    private static string ConvertODataOrderByToSQL(string orderby, Dictionary<string, string>? aliasToDb = null)
    {
        // Basic conversion for ORDER BY
        var sqlOrderBy = orderby.Replace(" desc", " DESC").Replace(" asc", " ASC");

        // If we have column mappings, try to translate aliases to database column names
        if (aliasToDb != null && aliasToDb.Count > 0)
        {
            foreach (var mapping in aliasToDb)
            {
                // Replace alias with database column name (case-insensitive)
                sqlOrderBy = Regex.Replace(sqlOrderBy, $@"\b{Regex.Escape(mapping.Key)}\b", mapping.Value, RegexOptions.IgnoreCase);
            }
        }

        return sqlOrderBy;
    }
}
