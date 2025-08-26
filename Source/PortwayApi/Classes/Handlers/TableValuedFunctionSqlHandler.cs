using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Dapper;
using Serilog;
using PortwayApi.Classes;
using PortwayApi.Classes.Helpers;

namespace PortwayApi.Classes.Handlers;

/// <summary>
/// Handler for Table Valued Function (TVF) SQL endpoints
/// </summary>
public static class TableValuedFunctionSqlHandler
{
    /// <summary>
    /// Handles GET requests for Table Valued Function endpoints
    /// </summary>
    /// <param name="endpoint">Endpoint definition</param>
    /// <param name="request">HTTP request</param>
    /// <param name="pathSegments">URL path segments after endpoint name</param>
    /// <param name="connectionString">Database connection string</param>
    /// <param name="odataParams">OData query parameters</param>
    /// <returns>Query results</returns>
    public static async Task<(bool Success, IActionResult? Result, List<object>? Data)> HandleTVFGetRequest(
        EndpointDefinition endpoint,
        HttpRequest request,
        string[] pathSegments,
        string connectionString,
        Dictionary<string, string> odataParams)
    {
        try
        {
            Log.Debug("🔄 Processing TVF GET request for: {FunctionName}", endpoint.DatabaseObjectName);

            // Validate that this is actually a TVF endpoint
            if (!TableValuedFunctionHelper.IsTableValuedFunction(endpoint))
            {
                Log.Warning("❌ Endpoint {Name} is not configured as a Table Valued Function", endpoint.DatabaseObjectName);
                return (false, new BadRequestObjectResult(new { 
                    error = "This endpoint is not configured as a Table Valued Function",
                    success = false 
                }), null);
            }

            // Check if function parameters are defined
            if (endpoint.FunctionParameters == null || endpoint.FunctionParameters.Count == 0)
            {
                Log.Warning("❌ TVF endpoint {Name} has no function parameters defined", endpoint.DatabaseObjectName);
                return (false, new BadRequestObjectResult(new { 
                    error = "Table Valued Function parameters are not configured",
                    success = false 
                }), null);
            }

            // Extract parameter values from the request
            var (parameterValues, extractionErrors) = TableValuedFunctionHelper.ExtractParameterValues(
                endpoint.FunctionParameters,
                request,
                pathSegments);

            if (extractionErrors.Any())
            {
                Log.Warning("❌ Parameter extraction errors for TVF {Name}: {Errors}", 
                    endpoint.DatabaseObjectName, string.Join(", ", extractionErrors));
                
                return (false, new BadRequestObjectResult(new { 
                    error = "Parameter validation failed",
                    details = extractionErrors,
                    success = false 
                }), null);
            }

            // Build the function call SQL
            var schema = endpoint.DatabaseSchema ?? "dbo";
            var functionName = endpoint.DatabaseObjectName!;
            
            var (functionCall, sqlParameters) = TableValuedFunctionHelper.BuildFunctionCall(
                schema,
                functionName,
                parameterValues,
                endpoint.FunctionParameters);

            // Get column mappings from AllowedColumns (using existing system)
            var (aliasToDb, dbToAlias) = PortwayApi.Classes.Helpers.ColumnMappingHelper.ParseColumnMappings(endpoint.AllowedColumns);

            // Hybrid OData support for TVFs
            string finalQuery = functionCall;
            Dictionary<string, object> odataSqlParams = new();
            if (odataParams.Any(p => !string.IsNullOrEmpty(p.Value) && (p.Key == "select" || p.Key == "filter" || p.Key == "orderby" || p.Key == "top" || p.Key == "skip")))
            {
                // Get the OData-to-SQL converter from DI
                var httpContext = request.HttpContext;
                var odataConverter = httpContext.RequestServices.GetService(typeof(PortwayApi.Interfaces.IODataToSqlConverter)) as PortwayApi.Interfaces.IODataToSqlConverter;
                if (odataConverter == null)
                {
                    Log.Error("❌ IODataToSqlConverter not available in DI");
                    return (false, new ObjectResult(new { error = "OData-to-SQL converter not available", success = false }) { StatusCode = 500 }, null);
                }

                // Use a dummy table name to generate OData SQL
                var dummyTable = "ODataDummy";
                var (odataQuery, odataParamsDict) = odataConverter.ConvertToSQL(dummyTable, odataParams);
                odataSqlParams = odataParamsDict;

                // Extract WHERE, ORDER BY, OFFSET/FETCH from the generated SQL
                string whereClause = string.Empty;
                string orderByClause = string.Empty;
                string offsetClause = string.Empty;
                var whereIdx = odataQuery.IndexOf(" WHERE ", StringComparison.OrdinalIgnoreCase);
                var orderByIdx = odataQuery.IndexOf(" ORDER BY ", StringComparison.OrdinalIgnoreCase);
                var offsetIdx = odataQuery.IndexOf(" OFFSET ", StringComparison.OrdinalIgnoreCase);

                if (whereIdx >= 0)
                {
                    if (orderByIdx > whereIdx)
                        whereClause = odataQuery.Substring(whereIdx, orderByIdx - whereIdx);
                    else if (offsetIdx > whereIdx)
                        whereClause = odataQuery.Substring(whereIdx, offsetIdx - whereIdx);
                    else
                        whereClause = odataQuery.Substring(whereIdx);
                }
                if (orderByIdx >= 0)
                {
                    if (offsetIdx > orderByIdx)
                        orderByClause = odataQuery.Substring(orderByIdx, offsetIdx - orderByIdx);
                    else
                        orderByClause = odataQuery.Substring(orderByIdx);
                }
                if (offsetIdx >= 0)
                {
                    offsetClause = odataQuery.Substring(offsetIdx);
                }

                // Rebuild the TVF query with OData fragments
                finalQuery = functionCall;
                if (!string.IsNullOrEmpty(whereClause))
                    finalQuery += whereClause;
                if (!string.IsNullOrEmpty(orderByClause))
                    finalQuery += orderByClause;
                if (!string.IsNullOrEmpty(offsetClause))
                    finalQuery += offsetClause;

                Log.Debug("Applied OData fragments to TVF: {Query}", finalQuery);
            }

            // Execute the query
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            // Merge TVF parameters and OData parameters
            var mergedParams = new Dictionary<string, object>(sqlParameters);
            foreach (var kvp in odataSqlParams)
                mergedParams[kvp.Key] = kvp.Value;

            Log.Debug("Executing TVF query: {Query} with parameters: {Parameters}", 
                finalQuery, string.Join(", ", mergedParams.Select(p => $"{p.Key}={p.Value}")));

            var results = await connection.QueryAsync(finalQuery, mergedParams);
            var resultList = results.Cast<object>().ToList();

            // Apply column alias transformations (database column names -> aliases)
            var (_, resultDbToAlias) = PortwayApi.Classes.Helpers.ColumnMappingHelper.ParseColumnMappings(endpoint.AllowedColumns);
            if (resultDbToAlias.Count > 0)
            {
                var aliasedResults = PortwayApi.Classes.Helpers.ColumnMappingHelper.TransformQueryResultsToAliases(resultList, resultDbToAlias);
                resultList = aliasedResults.Cast<object>().ToList();
                Log.Debug("Applied column alias transformations to {Count} TVF results", resultList.Count);
            }

            Log.Debug("✅ TVF query executed successfully. Returned {Count} records", resultList.Count);

            return (true, null, resultList);
        }
        catch (SqlException sqlEx)
        {
            Log.Error(sqlEx, "❌ SQL Error executing TVF {FunctionName}: {ErrorMessage}", 
                endpoint.DatabaseObjectName, sqlEx.Message);

            string errorMessage = sqlEx.Number switch
            {
                208 => "Table valued function does not exist or is not accessible",
                2812 => "Table valued function could not be found", 
                201 => "Invalid parameter count for table valued function",
                8114 => "Data type conversion error in function parameters",
                _ => "Error executing table valued function"
            };

            return (false, new ObjectResult(new { 
                error = errorMessage,
                success = false 
            }) { StatusCode = 500 }, null);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ Unexpected error executing TVF {FunctionName}", endpoint.DatabaseObjectName);
            
            return (false, new ObjectResult(new { 
                error = "An unexpected error occurred while executing the table valued function",
                success = false 
            }) { StatusCode = 500 }, null);
        }
    }

    /// <summary>
    /// Validates that a TVF endpoint configuration is correct
    /// </summary>
    /// <param name="endpoint">Endpoint definition to validate</param>
    /// <returns>List of validation errors</returns>
    public static List<string> ValidateTVFConfiguration(EndpointDefinition endpoint)
    {
        var errors = new List<string>();

        if (!TableValuedFunctionHelper.IsTableValuedFunction(endpoint))
        {
            errors.Add("DatabaseObjectType must be 'TableValuedFunction' for TVF endpoints");
        }

        if (string.IsNullOrEmpty(endpoint.DatabaseObjectName))
        {
            errors.Add("DatabaseObjectName (function name) is required for TVF endpoints");
        }

        if (endpoint.FunctionParameters == null || endpoint.FunctionParameters.Count == 0)
        {
            errors.Add("FunctionParameters must be defined for TVF endpoints");
        }
        else
        {
            // Validate each parameter
            for (int i = 0; i < endpoint.FunctionParameters.Count; i++)
            {
                var param = endpoint.FunctionParameters[i];
                var paramErrors = ValidateTVFParameter(param, i);
                errors.AddRange(paramErrors);
            }

            // Check for duplicate parameter names
            var duplicateNames = endpoint.FunctionParameters
                .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key);

            foreach (var duplicateName in duplicateNames)
            {
                errors.Add($"Duplicate parameter name: {duplicateName}");
            }

            // Check for duplicate path positions
            var pathParams = endpoint.FunctionParameters.Where(p => p.Source.Equals("path", StringComparison.OrdinalIgnoreCase));
            var duplicatePositions = pathParams
                .GroupBy(p => p.Position)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key);

            foreach (var duplicatePosition in duplicatePositions)
            {
                errors.Add($"Duplicate path position: {duplicatePosition}");
            }
        }

        // TVF endpoints should only support GET method
        if (endpoint.Methods?.Any(m => !m.Equals("GET", StringComparison.OrdinalIgnoreCase)) == true)
        {
            errors.Add("Table Valued Function endpoints should only support GET method");
        }

        return errors;
    }

    /// <summary>
    /// Validates a single TVF parameter configuration
    /// </summary>
    private static List<string> ValidateTVFParameter(TVFParameter parameter, int index)
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(parameter.Name))
        {
            errors.Add($"Parameter {index}: Name is required");
        }

        if (string.IsNullOrEmpty(parameter.SqlType))
        {
            errors.Add($"Parameter {parameter.Name}: SqlType is required");
        }

        var validSources = new[] { "path", "query", "header" };
        if (string.IsNullOrEmpty(parameter.Source) || 
            !validSources.Contains(parameter.Source, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add($"Parameter {parameter.Name}: Source must be one of: {string.Join(", ", validSources)}");
        }

        if (parameter.Source.Equals("path", StringComparison.OrdinalIgnoreCase))
        {
            if (parameter.Position <= 0)
            {
                errors.Add($"Parameter {parameter.Name}: Position must be greater than 0 for path parameters");
            }
        }
        else if (parameter.Source.Equals("header", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(parameter.HeaderName) && string.IsNullOrEmpty(parameter.Name))
            {
                errors.Add($"Parameter {parameter.Name}: HeaderName must be specified for header parameters");
            }
        }

        // Validate required parameters have no default value conflicts
        if (parameter.Required && !string.IsNullOrEmpty(parameter.DefaultValue))
        {
            Log.Warning("Parameter {Name} is marked as required but has a default value. Default value will be used if parameter is missing.", parameter.Name);
        }

        return errors;
    }

    /// <summary>
    /// Builds URL pattern documentation for a TVF endpoint
    /// </summary>
    /// <param name="endpoint">TVF endpoint definition</param>
    /// <returns>URL pattern examples</returns>
    public static List<string> BuildUrlPatternExamples(EndpointDefinition endpoint)
    {
        var examples = new List<string>();

        if (endpoint.FunctionParameters == null || endpoint.FunctionParameters.Count == 0)
        {
            return examples;
        }

        var pathParams = endpoint.FunctionParameters
            .Where(p => p.Source.Equals("path", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Position)
            .ToList();

        var queryParams = endpoint.FunctionParameters
            .Where(p => p.Source.Equals("query", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var headerParams = endpoint.FunctionParameters
            .Where(p => p.Source.Equals("header", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Build base URL with path parameters
        var urlBuilder = new System.Text.StringBuilder($"/api/{{env}}/{endpoint.DatabaseObjectName}");

        foreach (var pathParam in pathParams)
        {
            urlBuilder.Append($"/{{{pathParam.Name}}}");
        }

        // Add query parameters
        if (queryParams.Any())
        {
            urlBuilder.Append("?");
            var queryParts = queryParams.Select(p => 
                $"{p.QueryParameterName ?? p.Name}={{{p.Name}}}");
            urlBuilder.Append(string.Join("&", queryParts));
        }

        examples.Add($"URL Pattern: {urlBuilder}");

        // Add specific examples
        if (pathParams.Any())
        {
            var exampleUrl = $"/api/prod/{endpoint.DatabaseObjectName}";
            foreach (var pathParam in pathParams)
            {
                exampleUrl += $"/{GetExampleValue(pathParam)}";
            }

            if (queryParams.Any())
            {
                exampleUrl += "?";
                var exampleQueryParts = queryParams.Select(p => 
                    $"{p.QueryParameterName ?? p.Name}={GetExampleValue(p)}");
                exampleUrl += string.Join("&", exampleQueryParts);
            }

            examples.Add($"Example: {exampleUrl}");
        }

        if (headerParams.Any())
        {
            examples.Add("Required Headers:");
            foreach (var headerParam in headerParams)
            {
                examples.Add($"  {headerParam.HeaderName ?? headerParam.Name}: {GetExampleValue(headerParam)}");
            }
        }

        return examples;
    }

    /// <summary>
    /// Gets an example value for a parameter based on its SQL type
    /// </summary>
    private static string GetExampleValue(TVFParameter parameter)
    {
        var sqlType = parameter.SqlType.ToUpper();

        return sqlType switch
        {
            var t when t.StartsWith("INT") => "123",
            var t when t.StartsWith("BIGINT") => "123456789",
            var t when t.StartsWith("DECIMAL") || t.StartsWith("FLOAT") => "123.45",
            var t when t.StartsWith("DATETIME") || t.StartsWith("DATE") => "2024-01-01",
            var t when t.StartsWith("UNIQUEIDENTIFIER") => "12345678-1234-1234-1234-123456789012",
            var t when t.StartsWith("BIT") => "true",
            _ => $"example{parameter.Name}"
        };
    }
}
