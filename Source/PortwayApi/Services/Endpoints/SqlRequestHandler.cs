namespace PortwayApi.Services;

using System.Data;
using System.Data.Common;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using PortwayApi.Classes;
using PortwayApi.Services.Providers;
using PortwayApi.Helpers;
using PortwayApi.Interfaces;
using Serilog;

/// <summary>Executes SQL endpoint requests (OData reads, procedure writes) outside the controller</summary>
public sealed class SqlRequestHandler
{
    private readonly IODataToSqlConverter _oDataToSqlConverter;
    private readonly IEnvironmentSettingsProvider _environmentSettingsProvider;
    private readonly SqlConnectionPoolService _connectionPoolService;
    private readonly Services.Caching.CacheManager _cacheManager;

    public SqlRequestHandler(
        IODataToSqlConverter oDataToSqlConverter,
        IEnvironmentSettingsProvider environmentSettingsProvider,
        SqlConnectionPoolService connectionPoolService,
        Services.Caching.CacheManager cacheManager)
    {
        _oDataToSqlConverter = oDataToSqlConverter;
        _environmentSettingsProvider = environmentSettingsProvider;
        _connectionPoolService = connectionPoolService;
        _cacheManager = cacheManager;
    }

    /// <summary>Handles SQL GET requests</summary>
    public async Task<IActionResult> HandleSqlGetRequest(
        HttpContext context,
        EndpointDefinition endpoint,
        string env,
        string endpointName,
        string? id,
        string? remainingPath,
        string? select,
        string? filter,
        string? orderby,
        int top,
        int skip,
        string httpMethod = "GET")
    {
        var url = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}{context.Request.QueryString}";
        Log.Debug("SQL Query Request: {Url}", url);

        try
        {
            // Check if this is a SQL endpoint - if not, return 404
            // Step 1: Validate environment
            var (connectionString, serverName, _) = await _environmentSettingsProvider.LoadEnvironmentOrThrowAsync(env);
            if (string.IsNullOrEmpty(connectionString))
            {
                return PortwayResults.BadRequest($"Invalid or missing environment: {env}");
            }


            // Step 2.1: Apply endpoint-specific property overrides
            top = ApplyMaxPageSizeLimit(top, endpoint);
            orderby = ApplyDefaultSorting(orderby, endpoint);

            // Check if this is a Table Valued Function endpoint
            if (PortwayApi.Helpers.TableValuedFunctionHelper.IsTableValuedFunction(endpoint))
            {
                Log.Debug("Detected Table Valued Function endpoint: {FunctionName}", endpoint.DatabaseObjectName);
                
                // Extract path segments for parameter values
                var pathSegments = string.IsNullOrEmpty(remainingPath) 
                    ? new string[0] 
                    : remainingPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

                // Prepare OData parameters for TVF handling
                var tvfODataParams = new Dictionary<string, string>
                {
                    { "top", (top + 1).ToString() }, // +1 for pagination detection
                    { "skip", skip.ToString() }
                };

                if (!string.IsNullOrEmpty(select)) 
                    tvfODataParams["select"] = select;
                if (!string.IsNullOrEmpty(filter)) 
                    tvfODataParams["filter"] = filter;
                if (!string.IsNullOrEmpty(orderby)) 
                    tvfODataParams["orderby"] = orderby;

                // Handle TVF request using the dedicated handler
                var tvfResult = await PortwayApi.Classes.Handlers.TableValuedFunctionSqlHandler.HandleTVFGetRequest(
                    endpoint,
                    context.Request,
                    pathSegments,
                    _connectionPoolService.OptimizeConnectionString(connectionString),
                    tvfODataParams);

                bool tvfSuccess = tvfResult.Item1;
                IActionResult? tvfActionResult = tvfResult.Item2;
                List<object>? tvfData = tvfResult.Item3;

                if (!tvfSuccess)
                {
                    return tvfActionResult!;
                }

                // Process results for pagination and response formatting
                var tvfResultList = tvfData!;

                // Determine if it's the last page
                bool tvfIsLastPage = tvfResultList.Count <= top;
                if (!tvfIsLastPage)
                {
                    // Remove the extra row used for pagination
                    tvfResultList.RemoveAt(tvfResultList.Count - 1);
                }

                // Apply declarative response transforms to TVF rows
                if (endpoint.ResponseTransforms is { HasRules: true } tvfTransforms)
                {
                    tvfResultList = ResponseTransformHelper.ApplyToRows(tvfResultList, tvfTransforms);
                }

                // For ID-based requests (if applicable to TVF), return single item directly (OData convention)
                if (!string.IsNullOrEmpty(id))
                {
                    if (tvfResultList.Count == 0)
                    {
                        return PortwayResults.NotFound("No record found with specified parameters");
                    }

                    return new OkObjectResult(tvfResultList.FirstOrDefault());
                }

                // Return collection response with pagination headers
                ResponseHeaderHelper.SetPaginationHeaders(context, null, tvfResultList.Count, !tvfIsLastPage);
                
                // Only set Cache-Control header if caching is enabled for this endpoint
                if (IsCacheEnabled(endpoint))
                {
                    var tvfCacheDurationSeconds = GetCacheDurationMinutes(endpoint) * 60;
                    ResponseHeaderHelper.SetCacheControlHeader(context, tvfCacheDurationSeconds);
                }
                
                Log.Debug("Successfully processed TVF query for {FunctionName}", endpoint.DatabaseObjectName);
                return PortwayResults.Collection(tvfResultList,
                    tvfIsLastPage ? null : BuildNextLink(env, endpointName, select, filter, orderby, top, skip));
            }

            // Step 3: Extract endpoint details
            var schema = endpoint.DatabaseSchema ?? "dbo";
            var objectName = endpoint.DatabaseObjectName;
            var allowedColumns = endpoint.AllowedColumns ?? new List<string>();
            var allowedMethods = endpoint.Methods ?? new List<string> { "GET" };
            var primaryKey = endpoint.PrimaryKey ?? "Id";

            // Check if the incoming read method (GET or QUERY) is allowed
            if (!allowedMethods.Contains(httpMethod, StringComparer.OrdinalIgnoreCase))
            {
                return PortwayResults.MethodNotAllowed();
            }

            // Step 4: Handle ID-based filtering
            if (!string.IsNullOrEmpty(id))
            {
                // Get the actual database column name for the primary key; The primaryKey could be an alias, so we need to resolve it to the database column name
                string actualPrimaryKey = primaryKey;
                
                if (allowedColumns.Count > 0)
                {
                    var aliasToDatabase = endpoint.AliasToDatabase;
                    if (aliasToDatabase.TryGetValue(primaryKey, out var databasePrimaryKey))
                    {
                        actualPrimaryKey = databasePrimaryKey;
                        Log.Debug("Converted primary key alias '{Alias}' to database column '{DatabaseColumn}'", primaryKey, actualPrimaryKey);
                    }
                }
                
                // Create appropriate filter expression by primary key; Check if the ID is a GUID
                if (Guid.TryParse(id, out _))
                {
                    filter = $"{actualPrimaryKey} eq guid'{id}'";
                }
                else
                {
                    // Handle numeric or string IDs
                    bool isNumeric = long.TryParse(id, out _);
                    filter = isNumeric 
                        ? $"{actualPrimaryKey} eq {id}" 
                        : $"{actualPrimaryKey} eq '{id}'";
                }

                // Set top to 1 to return only one record when requesting by ID
                top = 1;
                
                Log.Debug("Created filter for ID-based query: {Filter}", filter);
            }

            // Step 5: Handle column aliases and validation
            string? selectForQuery = select; // This will contain database column names for the SQL query
            string? filterForQuery = filter; // This will contain database column names for the SQL query
            string? orderbyForQuery = orderby; // This will contain database column names for the SQL query
            
            if (allowedColumns.Count > 0)
            {
                // Get column mappings for alias support
                var aliasToDatabase = endpoint.AliasToDatabase;
                var databaseToAlias = endpoint.DatabaseToAlias;
                
                // Validate select columns (using aliases)
                if (!string.IsNullOrEmpty(select))
                {
                    var (isValid, invalidAliases) = PortwayApi.Helpers.ColumnMappingHelper.ValidateAliasColumns(select, aliasToDatabase);

                    if (!isValid)
                    {
                        return PortwayResults.BadRequest($"Selected columns not allowed: {string.Join(", ", invalidAliases)}");
                    }
                    
                    // Convert aliases to database column names for the SQL query
                    selectForQuery = PortwayApi.Helpers.ColumnMappingHelper.ConvertAliasesToDatabaseColumns(select, aliasToDatabase);
                    Log.Debug("Converted aliases '{Aliases}' to database columns '{DatabaseColumns}'", select, selectForQuery);
                }
                else
                {
                    // If no select and columns are restricted, use all allowed database columns
                    var allDatabaseColumns = PortwayApi.Helpers.ColumnMappingHelper.GetDatabaseColumns(databaseToAlias);
                    selectForQuery = string.Join(",", allDatabaseColumns);
                    Log.Debug("No select specified, using all allowed database columns: {DatabaseColumns}", selectForQuery);
                }
                
                // Convert filter column references from aliases to database columns
                if (!string.IsNullOrEmpty(filter))
                {
                    filterForQuery = PortwayApi.Helpers.ColumnMappingHelper.ConvertODataFilterAliases(filter, aliasToDatabase);
                    if (filterForQuery != filter)
                    {
                        Log.Debug("Converted filter aliases: '{OriginalFilter}' -> '{ConvertedFilter}'", filter, filterForQuery);
                    }
                }
                
                // Convert orderby column references from aliases to database columns
                if (!string.IsNullOrEmpty(orderby))
                {
                    orderbyForQuery = PortwayApi.Helpers.ColumnMappingHelper.ConvertODataOrderByAliases(orderby, aliasToDatabase);
                    if (orderbyForQuery != orderby)
                    {
                        Log.Debug("Converted orderby aliases: '{OriginalOrderBy}' -> '{ConvertedOrderBy}'", orderby, orderbyForQuery);
                    }
                }
            }

            // Step 6: Prepare OData parameters (using database column names)
            var odataParams = new Dictionary<string, string>
            {
                { "top", (top + 1).ToString() },
                { "skip", skip.ToString() }
            };

            if (!string.IsNullOrEmpty(selectForQuery)) 
                odataParams["select"] = selectForQuery;
            if (!string.IsNullOrEmpty(filterForQuery)) 
                odataParams["filter"] = filterForQuery;
            if (!string.IsNullOrEmpty(orderbyForQuery)) 
                odataParams["orderby"] = orderbyForQuery;

            // Step 7: Convert OData to SQL (provider-aware for correct dialect)
            var detectedProviderType = SqlProviderDetector.Detect(connectionString);
            var (query, parameters) = _oDataToSqlConverter.ConvertToSQL(
                $"{schema}.{objectName}",
                odataParams,
                detectedProviderType);

            // Step 8: Check cache first if enabled
            // $count=true adds the unpaged total matching the filter to the response
            bool countRequested = string.Equals(context.Request.Query["$count"].FirstOrDefault(), "true", StringComparison.OrdinalIgnoreCase);
            object? cachedResponse = null;
            string? cacheKey = null;
            bool cacheEnabled = IsCacheEnabled(endpoint);
            
            if (cacheEnabled)
            {
                // Create cache key based on query parameters
                cacheKey = $"sql:{env}:{endpointName}:{query.GetHashCode()}:{string.Join(",", parameters?.Select(p => $"{p.Key}={p.Value}") ?? new string[0])}:count={countRequested}";
                cachedResponse = await _cacheManager.GetAsync<object>(cacheKey);
                
                if (cachedResponse != null)
                {
                    Log.Debug("Cache hit for SQL query: {Endpoint}", endpointName);
                    return new OkObjectResult(cachedResponse);
                }
            }

            // Step 9: Execute query
            await using var connection = _connectionPoolService.CreateConnection(connectionString);
            await connection.OpenAsync();

            var results = await connection.QueryAsync(query, parameters);
            var resultList = results.ToList();

            // Step 10: Transform results to use aliases, but onlyy if column mappings exist
            var transformedResults = resultList;
            if (allowedColumns.Count > 0)
            {
                var databaseToAlias = endpoint.DatabaseToAlias;
                if (databaseToAlias.Count > 0)
                {
                    var aliasResults = PortwayApi.Helpers.ColumnMappingHelper.TransformQueryResultsToAliases(resultList, databaseToAlias);
                    transformedResults = aliasResults.Cast<object>().ToList();
                    Log.Debug("Transformed {Count} results from database columns to aliases", transformedResults.Count);
                }
            }

            // Apply declarative response transforms after alias mapping so rules target alias names
            if (endpoint.ResponseTransforms is { HasRules: true } sqlTransforms)
            {
                transformedResults = ResponseTransformHelper.ApplyToRows(transformedResults, sqlTransforms);
            }

            // Determine if it's the last page
            bool isLastPage = transformedResults.Count <= top;
            if (!isLastPage)
            {
                // Remove the extra row used for pagination
                transformedResults.RemoveAt(transformedResults.Count - 1);
            }

            // For ID-based requests, return the single item directly (OData convention)
            if (!string.IsNullOrEmpty(id))
            {
                // Return 404 if no results found
                if (transformedResults.Count == 0)
                {
                    return PortwayResults.NotFound($"No record found with {primaryKey} = {id}");
                }
                
                // Return the single item directly (OData convention - no wrapper)
                var singleItemResponse = transformedResults.FirstOrDefault();
                
                // Cache the single item response if caching is enabled
                if (cacheEnabled && !string.IsNullOrEmpty(cacheKey))
                {
                    var cacheDuration = GetCacheDurationMinutes(endpoint);
                    await _cacheManager.SetAsync(cacheKey, singleItemResponse, TimeSpan.FromMinutes(cacheDuration));
                    Log.Debug("Cached SQL single item response for {Endpoint}, duration: {Duration} minutes", endpointName, cacheDuration);
                }
                
                return new OkObjectResult(singleItemResponse);
            }

            // Step 10: Prepare response for collection requests with pagination headers
            ResponseHeaderHelper.SetPaginationHeaders(context, null, transformedResults.Count, !isLastPage);
            
            // Only set Cache-Control header if caching is enabled for this endpoint
            if (cacheEnabled)
            {
                var cacheDurationSeconds = GetCacheDurationMinutes(endpoint) * 60;
                ResponseHeaderHelper.SetCacheControlHeader(context, cacheDurationSeconds);
            }
            
            // Run the COUNT query only when the caller asked for it
            long? totalCount = null;
            if (countRequested)
            {
                var (countQuery, countParameters) = _oDataToSqlConverter.ConvertToCountSQL(
                    $"{schema}.{objectName}", odataParams, detectedProviderType);
                totalCount = await connection.ExecuteScalarAsync<long>(countQuery, countParameters);
            }

            var nextLink = isLastPage ? null : BuildNextLink(env, endpointName, select, filter, orderby, top, skip);
            var response = CollectionResponse<object>.Of(transformedResults, nextLink, totalCount);

            Log.Debug("Successfully processed query for {Endpoint}", endpointName);

            // Cache the response if caching is enabled
            if (cacheEnabled && !string.IsNullOrEmpty(cacheKey))
            {
                var cacheDuration = GetCacheDurationMinutes(endpoint);
                await _cacheManager.SetAsync(cacheKey, response, TimeSpan.FromMinutes(cacheDuration));
                Log.Debug("Cached SQL response for {Endpoint}, duration: {Duration} minutes", endpointName, cacheDuration);
            }

            return new OkObjectResult(response);
        }
        catch (SqlException sqlEx)
        {
            // Handle SQL-specific exceptions with more detail; Generate a masked error reference for troubleshooting
            var errorId = Guid.NewGuid().ToString("N").Substring(0, 8);
            Log.Error(sqlEx, "SQL Error [{ErrorId}] during query for endpoint: {EndpointName}. SQL Error Number: {ErrorNumber}, Severity: {Severity}, State: {State}, Message: {Message}",
                errorId, endpointName, sqlEx.Number, sqlEx.Class, sqlEx.State, sqlEx.Message);

            // Provide only generic error messages for all SQL errors to avoid leaking database details
            string errorMessage = sqlEx.Number switch
            {
                2 => $"A data error occurred. Please contact support with reference: T00{errorId}", // Connection error
                53 => $"A data error occurred. Please contact support with reference: T00{errorId}", // Network error
                208 => $"A data error occurred. Please contact support with reference: T0{errorId}", // Invalid object name
                547 => $"A data error occurred. Please contact support with reference: T{errorId}", // Constraint violation
                1205 => $"A data error occurred. Please contact support with reference: T{errorId}", // Deadlock
                2627 => $"A data error occurred. Please contact support with reference: T{errorId}", // Unique constraint
                2601 => $"A data error occurred. Please contact support with reference: T{errorId}", // Duplicate key
                4060 => $"A data error occurred. Please contact support with reference: T{errorId}", // Cannot open database
                8152 => $"A data error occurred. Please contact support with reference: T{errorId}", // Data too long
                8114 => $"A data error occurred. Please contact support with reference: T{errorId}", // Data conversion
                18456 => $"A data error occurred. Please contact support with reference: T{errorId}", // Login failed
                50000 => $"A data error occurred. Please contact support with reference: T{errorId}", // User-defined error
                _ => $"An error occurred while processing your request. Reference: H{errorId}"
            };

            return PortwayResults.ProblemWithTrace(context, errorMessage, "Internal Error");
        }
        catch (Exception ex) when (ex is not SqlException)
        {
            Log.Error(ex, "Error during SQL query for endpoint: {EndpointName}. Exception Type: {ExceptionType}",
                endpointName, ex.GetType().Name);
            return PortwayResults.ProblemWithTrace(context, "Error processing. Please check the logs for more details.", "Error");
        }
    }

	/// <summary>Handles SQL POST requests (Create)</summary>
    public async Task<IActionResult> HandleSqlPostRequest(
        HttpContext context,
        EndpointDefinition endpoint,
        string env,
        string endpointName,
        JsonElement data)
    {
        try
        {
            // Check if this is a SQL endpoint
            // Validate environment
            var (connectionString, serverName, _) = await _environmentSettingsProvider.LoadEnvironmentOrThrowAsync(env);
            if (string.IsNullOrEmpty(connectionString))
            {
                return PortwayResults.BadRequest($"Invalid or missing environment: {env}");
            }


            // Check method support and procedure definition
            if (!(endpoint.Methods?.Contains("POST") ?? false))
            {
                return PortwayResults.MethodNotAllowed("This endpoint does not support POST operations");
            }

            if (string.IsNullOrEmpty(endpoint.Procedure))
            {
                return PortwayResults.BadRequest("This endpoint does not support insert operations");
            }

            // Validate input data against allowed columns, required columns, and regex patterns
            var (isValid, errorMessage, validationErrors) = SqlInputValidator.Validate(data, endpoint, "POST");
            if (!isValid)
            {
                if (validationErrors != null && validationErrors.Any())
                {
                    return PortwayResults.ValidationFailed(validationErrors);
                }
                return PortwayResults.BadRequest(errorMessage ?? "Validation failed");
            }

            // Prepare stored procedure parameters
            var dynamicParams = new DynamicParameters();
            dynamicParams.Add("@Method", "INSERT");
            
            if (context.User.Identity?.Name != null)
            {
                dynamicParams.Add("@UserName", context.User.Identity.Name);
            }

            // Extract and add parameters
            foreach (var property in data.EnumerateObject())
            {
                dynamicParams.Add($"@{property.Name}", GetParameterValue(property.Value));
            }

            // Execute stored procedure
            await using var connection = _connectionPoolService.CreateConnection(connectionString);
            await connection.OpenAsync();
            
            // Special handling for SqlException to catch RAISERROR messages
            var (schema, procedureName) = ParseProcedureName(endpoint.Procedure);
            try
            {
                var result = await connection.QueryAsync(
                    $"[{schema}].[{procedureName}]",
                    dynamicParams,
                    commandType: CommandType.StoredProcedure
                );

                var resultList = result.ToList();
                
                Log.Debug("Successfully executed INSERT procedure for {Endpoint}", endpointName);
                
                return PortwayResults.Create($"/api/{env}/{endpointName}", "Record created successfully", resultList.FirstOrDefault());
            }
            catch (SqlException sqlEx) when (IsIntentionalUserError(sqlEx))
            {
                Log.Warning("Custom SQL error (RAISERROR) for {Endpoint}: {ErrorMessage}", endpointName, sqlEx.Message);
                return PortwayResults.BadRequest(sqlEx.Message);
            }
        }
        catch (SqlException sqlEx)
        {
            // Handle SQL-specific exceptions with more detail; Generate a masked error reference for troubleshooting
            var errorId = Guid.NewGuid().ToString("N").Substring(0, 8);
            Log.Error(sqlEx, "SQL Error [{ErrorId}] during query for endpoint: {EndpointName}. SQL Error Number: {ErrorNumber}, Severity: {Severity}, State: {State}, Message: {Message}",
                errorId, endpointName, sqlEx.Number, sqlEx.Class, sqlEx.State, sqlEx.Message);

            // Provide only generic error messages for all SQL errors to avoid leaking database details
            string errorMessage = sqlEx.Number switch
            {
                2 => $"A data error occurred. Please contact support with reference: T00{errorId}", // Connection error
                53 => $"A data error occurred. Please contact support with reference: T00{errorId}", // Network error
                208 => $"A data error occurred. Please contact support with reference: T0{errorId}", // Invalid object name
                547 => $"A data error occurred. Please contact support with reference: T{errorId}", // Constraint violation
                1205 => $"A data error occurred. Please contact support with reference: T{errorId}", // Deadlock
                2627 => $"A data error occurred. Please contact support with reference: T{errorId}", // Unique constraint
                2601 => $"A data error occurred. Please contact support with reference: T{errorId}", // Duplicate key
                4060 => $"A data error occurred. Please contact support with reference: T{errorId}", // Cannot open database
                8152 => $"A data error occurred. Please contact support with reference: T{errorId}", // Data too long
                8114 => $"A data error occurred. Please contact support with reference: T{errorId}", // Data conversion
                18456 => $"A data error occurred. Please contact support with reference: T{errorId}", // Login failed
                50000 => $"A data error occurred. Please contact support with reference: T{errorId}", // User-defined error
                _ => $"An error occurred while processing your request. Reference: H{errorId}"
            };

            return PortwayResults.ProblemWithTrace(context, errorMessage, "Internal Error");
        }
        catch (Exception ex)
        {
            if (ex is JsonException)
            {
                return PortwayResults.BadRequest("Invalid JSON format in request");
            }

            Log.Error(ex, "Error processing request for endpoint {EndpointName}: {ErrorType}: {ErrorMessage}",
                endpointName, ex.GetType().Name, ex.Message);

            return PortwayResults.ServerError(context, "An error occurred while processing your request");
        }
    }

 /// <summary>Determines if a SQL exception is an intentional user-facing error vs a system error</summary>
private bool IsIntentionalUserError(SqlException sqlEx)
{
    // Only error 50000 is the default for intentional RAISERROR without specific error number; Other error numbers in 50000+ range could be system or custom errors
    return sqlEx.Number == 50000;
}

    /// <summary>Handles SQL PUT requests (Update)</summary>
    public async Task<IActionResult> HandleSqlPutRequest(
        HttpContext context,
        EndpointDefinition endpoint,
        string env,
        string endpointName,
        JsonElement data)
    {
        try
        {
            // Check if this is a SQL endpoint - if not, return 404
            // Step 1: Validate environment
            var (connectionString, serverName, _) = await _environmentSettingsProvider.LoadEnvironmentOrThrowAsync(env);
            if (string.IsNullOrEmpty(connectionString))
            {
                return PortwayResults.BadRequest($"Invalid or missing environment: {env}");
            }


            // Step 3: Check if the endpoint supports PUT and has a procedure defined
            if (!(endpoint.Methods?.Contains("PUT") ?? false))
            {
                return PortwayResults.MethodNotAllowed();
            }

            if (string.IsNullOrEmpty(endpoint.Procedure))
            {
                return PortwayResults.BadRequest("This endpoint does not support update operations");
            }

            // Step 4: Validate input data against allowed columns, required columns, and regex patterns
            var (isValid, errorMessage, validationErrors) = SqlInputValidator.Validate(data, endpoint, "PUT");
            if (!isValid)
            {
                if (validationErrors != null && validationErrors.Any())
                {
                    return PortwayResults.ValidationFailed(validationErrors);
                }
                return PortwayResults.BadRequest(errorMessage ?? "Validation failed");
            }

            // Step 5: Validate that ID is present for update operations
            var (isParamsValid, paramsErrorMessage) = ValidateSqlParameters(data, "UPDATE");
            if (!isParamsValid)
            {
                return PortwayResults.BadRequest(paramsErrorMessage ?? "Validation failed");
            }

            // Step 6: Prepare stored procedure parameters
            var dynamicParams = new DynamicParameters();

            // Add method parameter (always needed for the standard procedure pattern)
            dynamicParams.Add("@Method", "UPDATE");

            // Add user parameter if available
            if (context.User.Identity?.Name != null)
            {
                dynamicParams.Add("@UserName", context.User.Identity.Name);
            }

            // Step 7: Extract and add data parameters from the request
            foreach (var property in data.EnumerateObject())
            {
                dynamicParams.Add($"@{property.Name}", GetParameterValue(property.Value));
            }

            // Step 8: Execute stored procedure
            await using var connection = _connectionPoolService.CreateConnection(connectionString);
            await connection.OpenAsync();

            // Special handling for SqlException to catch RAISERROR messages
            var (schema, procedureName) = ParseProcedureName(endpoint.Procedure);
            try
            {
                var result = await connection.QueryAsync(
                    $"[{schema}].[{procedureName}]",
                    dynamicParams,
                    commandType: CommandType.StoredProcedure
                );

                // Convert result to a list (could be empty if no rows returned)
                var resultList = result.ToList();

                Log.Debug("Successfully executed UPDATE procedure for {Endpoint}", endpointName);

                // Return the results, which typically includes the updated record
                return PortwayResults.Mutation("Record updated successfully", resultList.FirstOrDefault());
            }
            catch (SqlException sqlEx) when (IsIntentionalUserError(sqlEx))
            {
                Log.Warning("Custom SQL error (RAISERROR) for {Endpoint}: {ErrorMessage}", endpointName, sqlEx.Message);
                return PortwayResults.BadRequest(sqlEx.Message);
            }
        }
        catch (SqlException sqlEx)
        {
            Log.Error(sqlEx, "SQL Exception for {Endpoint}: {ErrorCode}, {ErrorMessage}",
                endpointName, sqlEx.Number, sqlEx.Message);
            return PortwayResults.ServerError(context, "Internal operation failed");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error processing UPDATE for {Endpoint}", endpointName);
            return PortwayResults.ServerError(context, "An error occurred while processing your request");
        }
    }

    /// <summary>Handles SQL PATCH requests (partial updates)</summary>
    public async Task<IActionResult> HandleSqlPatchRequest(
        HttpContext context,
        EndpointDefinition endpoint,
        string env,
        string endpointName,
        JsonDocument requestBody)
    {
        try
        {
            // Step 1: Check if this is a SQL endpoint
            // Step 2: Validate environment
            var (connectionString, serverName, _) = await _environmentSettingsProvider.LoadEnvironmentOrThrowAsync(env);
            if (string.IsNullOrEmpty(connectionString))
            {
                return PortwayResults.BadRequest($"Invalid or missing environment: {env}");
            }


            if (!(endpoint.Methods?.Contains("PATCH") ?? false))
            {
                return PortwayResults.MethodNotAllowed();
            }

            if (string.IsNullOrEmpty(endpoint.Procedure))
            {
                return PortwayResults.BadRequest("This endpoint does not support partial update operations");
            }

            // Step 4: Parse and validate request body
            var data = requestBody.RootElement;

            // Validate against allowed columns, required columns, and regex patterns
            var (isValid, errorMessage, validationErrors) = SqlInputValidator.Validate(data, endpoint, "PATCH");
            if (!isValid)
            {
                if (validationErrors != null && validationErrors.Any())
                {
                    return PortwayResults.ValidationFailed(validationErrors);
                }
                return PortwayResults.BadRequest(errorMessage ?? "Validation failed");
            }

            // Step 5: Validate that required parameters are present (especially ID)
            var (isParamsValid, paramsErrorMessage) = ValidateSqlParameters(data, "UPDATE");
            if (!isParamsValid)
            {
                return PortwayResults.BadRequest(paramsErrorMessage ?? "Validation failed");
            }

            // Step 6: Prepare stored procedure parameters
            var dynamicParams = new DynamicParameters();

            // Add method parameter - use "PATCH" to differentiate from full UPDATE
            dynamicParams.Add("@Method", "PATCH");

            // Add user parameter if available
            if (context.User.Identity?.Name != null)
            {
                dynamicParams.Add("@UserName", context.User.Identity.Name);
            }

            // Step 7: Extract and add data parameters from the request. Only the fields provided in the request will be updated
            foreach (var property in data.EnumerateObject())
            {
                dynamicParams.Add($"@{property.Name}", GetParameterValue(property.Value));
            }

            // Step 8: Execute stored procedure
            await using var connection = _connectionPoolService.CreateConnection(connectionString);
            await connection.OpenAsync();

            // Special handling for SqlException to catch RAISERROR messages
            var (schema, procedureName) = ParseProcedureName(endpoint.Procedure);
            try
            {
                var result = await connection.QueryAsync(
                    $"[{schema}].[{procedureName}]",
                    dynamicParams,
                    commandType: CommandType.StoredProcedure
                );

                // Convert result to a list
                var resultList = result.ToList();

                Log.Debug("Successfully executed PATCH procedure for {Endpoint}", endpointName);

                // Return the results
                return PortwayResults.Mutation("Record partially updated successfully", resultList.FirstOrDefault());
            }
            catch (SqlException sqlEx) when (IsIntentionalUserError(sqlEx))
            {
                Log.Warning("Custom SQL error (RAISERROR) for {Endpoint}: {ErrorMessage}", endpointName, sqlEx.Message);
                return PortwayResults.BadRequest(sqlEx.Message);
            }
        }
        catch (SqlException sqlEx)
        {
            Log.Error(sqlEx, "SQL Exception for {Endpoint}: {ErrorCode}, {ErrorMessage}",
                endpointName, sqlEx.Number, sqlEx.Message);
            return PortwayResults.ServerError(context, "Internal operation failed");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error processing PATCH for {Endpoint}", endpointName);
            return PortwayResults.ServerError(context, "An error occurred while processing your request");
        }
    }

    /// <summary>Handles SQL DELETE requests</summary>
    public async Task<IActionResult> HandleSqlDeleteRequest(
        HttpContext context,
        EndpointDefinition endpoint,
        string env,
        string endpointName,
        string id)
    {
        try
        {
            // Check if this is a SQL endpoint - if not, return 404
            // Validate environment
            var (connectionString, serverName, _) = await _environmentSettingsProvider.LoadEnvironmentOrThrowAsync(env);
            if (string.IsNullOrEmpty(connectionString))
            {
                return PortwayResults.BadRequest($"Invalid or missing environment: {env}");
            }


            // Check if the endpoint supports DELETE and has a procedure defined
            if (!(endpoint.Methods?.Contains("DELETE") ?? false))
            {
                return PortwayResults.MethodNotAllowed();
            }

            if (string.IsNullOrEmpty(endpoint.Procedure))
            {
                return PortwayResults.BadRequest("This endpoint does not support delete operations");
            }

            // Check if the ID is provided
            if (string.IsNullOrEmpty(id))
            {
                return PortwayResults.BadRequest("ID parameter is required for delete operations");
            }

            // Prepare stored procedure parameters
            var dynamicParams = new DynamicParameters();

            // Add method parameter (always needed for the standard procedure pattern)
            dynamicParams.Add("@Method", "DELETE");

            // Handle different primary key parameter names
            var primaryKey = endpoint.PrimaryKey ?? "Id";
            dynamicParams.Add($"@{primaryKey}", id);

            // For backward compatibility, also add @id parameter
            if (!primaryKey.Equals("id", StringComparison.OrdinalIgnoreCase))
            {
                dynamicParams.Add("@id", id);
            }

            // Add user parameter if available
            if (context.User.Identity?.Name != null)
            {
                dynamicParams.Add("@UserName", context.User.Identity.Name);
            }

            // Execute stored procedure
            await using var connection = _connectionPoolService.CreateConnection(connectionString);
            await connection.OpenAsync();

            // Special handling for SqlException to catch RAISERROR messages
            var (schema, procedureName) = ParseProcedureName(endpoint.Procedure);
            try
            {
                var result = await connection.QueryAsync(
                    $"[{schema}].[{procedureName}]",
                    dynamicParams,
                    commandType: CommandType.StoredProcedure
                );

                // Convert result to a list (could be empty if no rows returned)
                var resultList = result.ToList();

                Log.Debug("Successfully executed DELETE procedure for {Endpoint}", endpointName);

                // Return the results, which typically includes deletion confirmation
                return PortwayResults.Mutation("Record deleted successfully", resultList.FirstOrDefault());
            }
            catch (SqlException sqlEx) when (IsIntentionalUserError(sqlEx))
            {
                Log.Warning("Custom SQL error (RAISERROR) for {Endpoint}: {ErrorMessage}", endpointName, sqlEx.Message);
                return PortwayResults.BadRequest(sqlEx.Message);
            }
        }
        catch (SqlException sqlEx)
        {
            Log.Error(sqlEx, "SQL Exception for {Endpoint}: {ErrorCode}, {ErrorMessage}",
                endpointName, sqlEx.Number, sqlEx.Message);
            return PortwayResults.ServerError(context, "Internal operation failed");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error processing DELETE for {Endpoint}", endpointName);
            return PortwayResults.ServerError(context, "An error occurred while processing your request");
        }
    }


    private static readonly string[] IdFieldNames = new[]
    {
        "id", "Id", "ID", "IdField", "IDField",
        "pk", "PK", "PrimaryKey", "primaryKey", "primarykey",
        "internalId", "InternalId", "InternalID", "internalid",
        "recordId", "RecordId"
    };

    /// <summary>Helper method to build next link for pagination</summary>
    private string BuildNextLink(
        string env, 
        string endpointPath, 
        string? select, 
        string? filter, 
        string? orderby, 
        int top, 
        int skip)
    {
        var nextLink = $"/api/{env}/{endpointPath}?$top={top}&$skip={skip + top}";

        if (!string.IsNullOrWhiteSpace(select))
            nextLink += $"&$select={Uri.EscapeDataString(select)}";
        
        if (!string.IsNullOrWhiteSpace(filter))
            nextLink += $"&$filter={Uri.EscapeDataString(filter)}";
        
        if (!string.IsNullOrWhiteSpace(orderby))
            nextLink += $"&$orderby={Uri.EscapeDataString(orderby)}";

        return nextLink;
    }

    /// <summary>Helper method to convert JsonElement to appropriate parameter value</summary>
    private static object? GetParameterValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt32(out int intValue) ? intValue
                : element.TryGetDouble(out double doubleValue) ? doubleValue
                : (object?)null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.ToString()
        };
    }

    /// <summary>Parses a stored procedure name into schema and procedure name parts. Handles both "schema.procedure" and plain "procedure" formats, stripping brackets</summary>
    private static (string Schema, string ProcedureName) ParseProcedureName(string procedure)
    {
        if (procedure.Contains('.'))
        {
            var parts = procedure.Split('.');
            return (parts[0].Trim('[', ']'), parts[1].Trim('[', ']'));
        }
        return ("dbo", procedure);
    }

    /// <summary>Applies the MaxPageSize limit from endpoint properties, overriding the requested top value if necessary</summary>
    private int ApplyMaxPageSizeLimit(int requestedTop, EndpointDefinition endpoint)
    {
        // Check if MaxPageSize is defined in endpoint properties
        if (endpoint.Properties?.TryGetValue("MaxPageSize", out var maxPageSizeObj) == true)
        {
            if (maxPageSizeObj is int maxPageSize)
            {
                Log.Debug("📏 Applying MaxPageSize limit: {MaxPageSize} for endpoint {Endpoint}", maxPageSize, endpoint.EndpointName);
                return Math.Min(requestedTop, maxPageSize);
            }
            else if (int.TryParse(maxPageSizeObj?.ToString(), out var parsedMaxPageSize))
            {
                Log.Debug("📏 Applying MaxPageSize limit: {MaxPageSize} for endpoint {Endpoint}", parsedMaxPageSize, endpoint.EndpointName);
                return Math.Min(requestedTop, parsedMaxPageSize);
            }
        }
        
        return requestedTop; // No limit defined, use original value
    }

    /// <summary>Applies the DefaultSort from endpoint properties if no orderby is provided</summary>
    private string? ApplyDefaultSorting(string? requestedOrderBy, EndpointDefinition endpoint)
    {
        // Only apply default if no orderby was requested
        if (!string.IsNullOrEmpty(requestedOrderBy))
        {
            return requestedOrderBy;
        }

        // Check if DefaultSort is defined in endpoint properties
        if (endpoint.Properties?.TryGetValue("DefaultSort", out var defaultSortObj) == true)
        {
            var defaultSort = defaultSortObj?.ToString();
            if (!string.IsNullOrEmpty(defaultSort))
            {
                Log.Debug("🔀 Applying DefaultSort: {DefaultSort} for endpoint {Endpoint}", defaultSort, endpoint.EndpointName);
                return defaultSort;
            }
        }

        return requestedOrderBy; // No default defined, use original value
    }

    /// <summary>Checks if caching is enabled for the endpoint</summary>
    private bool IsCacheEnabled(EndpointDefinition endpoint)
    {
        if (endpoint.Properties?.TryGetValue("CacheEnabled", out var cacheEnabledObj) == true)
        {
            if (cacheEnabledObj is bool cacheEnabled)
            {
                return cacheEnabled;
            }
            else if (bool.TryParse(cacheEnabledObj?.ToString(), out var parsedCacheEnabled))
            {
                return parsedCacheEnabled;
            }
        }

        // Fall back to global cache setting; CacheManager.GetAsync already guards
        // against the global Enabled=false case, so returning true here means
        // "defer to the global setting" rather than hard-disabling per endpoint
        return true;
    }

    /// <summary>Gets the cache duration in minutes for the endpoint</summary>
    private int GetCacheDurationMinutes(EndpointDefinition endpoint)
    {
        if (endpoint.Properties?.TryGetValue("CacheDurationMinutes", out var durationObj) == true)
        {
            if (durationObj is int duration)
            {
                return duration;
            }
            else if (int.TryParse(durationObj?.ToString(), out var parsedDuration))
            {
                return parsedDuration;
            }
        }
        
        return 5; // Default: 5 minutes
    }

    /// <summary>Validates SQL parameters for update and delete operations</summary>
    private (bool IsValid, string? ErrorMessage) ValidateSqlParameters(JsonElement data, string operation)
    {

        if (operation is "UPDATE" or "DELETE")
        {
            bool hasId = IdFieldNames.Any(fieldName => data.TryGetProperty(fieldName, out _));
                        
            if (!hasId)
            {
                return (false, "ID field is required for this operation");
            }
        }
        
        return (true, null);
    }

}
