namespace PortwayApi.Services;

using System.Collections.Frozen;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using PortwayApi.Classes;
using PortwayApi.Services.Providers;
using PortwayApi.Helpers;
using PortwayApi.Interfaces;
using Serilog;

/// <summary>
/// Executes SQL endpoint requests (OData reads, procedure writes) outside the controller
/// </summary>
public sealed partial class SqlRequestHandler
{
    private readonly IODataToSqlConverter _oDataToSqlConverter;
    private readonly IEnvironmentSettingsProvider _environmentSettingsProvider;
    private readonly SqlConnectionPoolService _connectionPoolService;
    private readonly Services.Caching.CacheManager _cacheManager;

    public SqlRequestHandler(
        IODataToSqlConverter oDataToSqlConverter,
        IEnvironmentSettingsProvider environmentSettingsProvider,
        SqlConnectionPoolService connectionPoolService,
        Services.Caching.CacheManager cacheManager,
        ISqlProviderFactory providerFactory)
    {
        _oDataToSqlConverter = oDataToSqlConverter;
        _environmentSettingsProvider = environmentSettingsProvider;
        _connectionPoolService = connectionPoolService;
        _cacheManager = cacheManager;
        _providerFactory = providerFactory;
    }

    private readonly ISqlProviderFactory _providerFactory;

    /// <summary>
    /// Runs a write procedure with the dialect-correct invocation for the environment's provider
    /// </summary>
    private async Task<List<dynamic>> ExecuteProcedureAsync(
        DbConnection connection, string connectionString, string procedure, DynamicParameters parameters)
    {
        var provider = _providerFactory.GetProvider(connectionString);
        var (schema, name) = ParseProcedureName(procedure, provider, connection);

        await provider.AddMissingProcedureArgumentsAsync(connection, schema, name, parameters, CancellationToken.None);

        var (commandText, commandType) = provider.BuildProcedureInvocation(schema, name, parameters.ParameterNames.ToList());
        var rows = await connection.QueryAsync(commandText, parameters, commandType: commandType);
        return rows.ToList();
    }

    private const string InvalidODataMessage = "Invalid OData query. Check $filter, $select, $orderby and $expand syntax.";

    /// <summary>
    /// Handles SQL GET requests
    /// </summary>
    public async Task<IActionResult> HandleSqlGetRequest(
        HttpContext context,
        EndpointDefinition endpoint,
        string env,
        string endpointName,
        string? id,
        string? remainingPath,
        ODataReadOptions options,
        string httpMethod = "GET",
        IReadOnlyDictionary<string, string>? tenants = null)
    {
        if (options.Top < 0 || options.Skip < 0)
            return PortwayResults.BadRequest("$top and $skip must be zero or greater");

        var (select, filter, orderby, expandRaw, countRequested, top, skip) = options;
        var tenantPredicates = TenantSql.Predicates(endpoint, tenants ?? FrozenDictionary<string, string>.Empty);
        if (endpoint.HasTenancy && tenantPredicates.Count == 0)
            return PortwayResults.Forbidden("This endpoint requires tenant access");

        var url = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}{context.Request.QueryString}";
        Log.Debug("SQL Query Request: {Url}", UrlRedaction.Redact(url));

        try
        {
            // Validate environment
            var (connectionString, serverName, _) = await _environmentSettingsProvider.LoadEnvironmentOrThrowAsync(env);
            if (string.IsNullOrEmpty(connectionString))
            {
                return PortwayResults.BadRequest($"Invalid or missing environment: {env}");
            }


            if (endpoint.AllowedColumns is { Count: > 0 })
            {
                var refused = ColumnMappingHelper.FindDisallowedFilterColumns(filter, endpoint.AliasToDatabase)
                    .Concat(ColumnMappingHelper.FindDisallowedOrderByColumns(orderby, endpoint.AliasToDatabase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (refused.Contains(ColumnMappingHelper.InvalidExpression))
                    return PortwayResults.BadRequest(InvalidODataMessage);
                if (refused.Count > 0)
                    return PortwayResults.BadRequest($"Columns not allowed: {string.Join(", ", refused)}");
            }

            // Step 2.1: Apply endpoint-specific property overrides
            top = Math.Min(ApplyMaxPageSizeLimit(top, endpoint), int.MaxValue - 1);
            orderby = ApplyDefaultSorting(orderby, endpoint);

            // Check if this is a Table Valued Function endpoint
            if (SqlTableValuedFunctionHelper.IsTableValuedFunction(endpoint))
            {
                // Fail closed: the TVF hybrid splice cannot include a JOIN, so $expand would be silently dropped
                if (!string.IsNullOrWhiteSpace(expandRaw))
                    return PortwayResults.BadRequest("$expand is not supported on table-valued function endpoints");

                Log.Debug("Detected Table Valued Function endpoint: {FunctionName}", endpoint.DatabaseObjectName);

                // Extract path segments for parameter values
                var pathSegments = string.IsNullOrEmpty(remainingPath)
                    ? new string[0]
                    : remainingPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

                // numeric segments are path values
                if (!string.IsNullOrEmpty(id))
                    pathSegments = [id, .. pathSegments];

                var (tvfSelect, tvfFilter, tvfOrderby) = (select, filter, orderby);
                var tvfColumnProblem = MapODataColumns(endpoint, ref tvfSelect, ref tvfFilter, ref tvfOrderby);
                if (tvfColumnProblem is not null)
                    return tvfColumnProblem;

                // Prepare OData parameters for TVF handling
                var tvfODataParams = new Dictionary<string, string>
                {
                    { "top", (top + 1).ToString() }, // +1 for pagination detection
                    { "skip", skip.ToString() }
                };

                if (!string.IsNullOrEmpty(tvfSelect))
                    tvfODataParams["select"] = tvfSelect;
                if (!string.IsNullOrEmpty(tvfFilter))
                    tvfODataParams["filter"] = tvfFilter;
                if (!string.IsNullOrEmpty(tvfOrderby))
                    tvfODataParams["orderby"] = tvfOrderby;

                // Handle TVF request using the dedicated handler
                var tvfResult = await Classes.Handlers.TableValuedFunctionSqlHandler.HandleTVFGetRequest(
                    endpoint,
                    context.Request,
                    pathSegments,
                    _connectionPoolService.OptimizeConnectionString(connectionString),
                    tvfODataParams,
                    _providerFactory.GetProvider(connectionString),
                    tenantPredicates);

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
                    tvfResultList = HttpResponseTransformHelper.ApplyToRows(tvfResultList, tvfTransforms);
                }

                // Return collection response with pagination headers
                HttpResponseHeaderHelper.SetPaginationHeaders(context, null, tvfResultList.Count, !tvfIsLastPage);

                // Only set Cache-Control header if caching is enabled for this endpoint
                if (IsCacheEnabled(endpoint))
                {
                    var tvfCacheDurationSeconds = GetCacheDurationMinutes(endpoint) * 60;
                    HttpResponseHeaderHelper.SetCacheControlHeader(context, tvfCacheDurationSeconds);
                }

                Log.Debug("Successfully processed TVF query for {FunctionName}", endpoint.DatabaseObjectName);
                return PortwayResults.Collection(tvfResultList,
                    tvfIsLastPage ? null : BuildNextLink(env, endpointName, options, top, skip, pathSegments));
            }

            // Step 3: Extract endpoint details
            var schema = endpoint.DatabaseSchema ?? "dbo";
            var objectName = endpoint.DatabaseObjectName;
            var allowedColumns = endpoint.AllowedColumns ?? new List<string>();
            var allowedMethods = endpoint.Methods ?? new List<string> { "GET" };

            // Check if the incoming read method (GET or QUERY) is allowed
            if (!allowedMethods.Contains(httpMethod, StringComparer.OrdinalIgnoreCase))
            {
                return PortwayResults.MethodNotAllowed();
            }

            // Step 4: Handle ID-based filtering
            if (!string.IsNullOrEmpty(id))
            {
                if (endpoint.RecordKeyColumn is not { } actualPrimaryKey)
                {
                    return PortwayResults.BadRequest("This endpoint does not support record keys");
                }

                var keyCoerces = _providerFactory.GetProvider(connectionString).CoercesTextKeys;

                // literal coerces to column type
                if (Guid.TryParseExact(id, "D", out _))
                {
                    filter = null;
                    tenantPredicates = [.. tenantPredicates, new TenantPredicate(actualPrimaryKey, id)];
                    if (keyCoerces)
                        tenantPredicates = [.. tenantPredicates, new TenantPredicate(actualPrimaryKey, id, ExactText: true)];
                }
                else
                {
                    // Handle numeric or string IDs
                    bool isNumeric = long.TryParse(id, out _);
                    filter = isNumeric
                        ? $"{actualPrimaryKey} eq {id}"
                        : $"{actualPrimaryKey} eq '{id}'";
                    if (keyCoerces && !isNumeric)
                        tenantPredicates = [.. tenantPredicates, new TenantPredicate(actualPrimaryKey, id, ExactText: true)];
                }

                // Set top to 1 to return only one record when requesting by ID
                top = 1;

                Log.Debug("Created filter for ID-based query: {Filter}", filter);
            }

            // Step 5: Handle column aliases and validation
            string? selectForQuery = select; // This will contain database column names for the SQL query
            string? filterForQuery = filter; // This will contain database column names for the SQL query
            string? orderbyForQuery = orderby; // This will contain database column names for the SQL query

            var columnProblem = MapODataColumns(endpoint, ref selectForQuery, ref filterForQuery, ref orderbyForQuery);
            if (columnProblem is not null)
                return columnProblem;

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

            // Step 6b: $expand to-one navigations. SQL read path only, gated and capability-checked
            List<(string NavName, IReadOnlyDictionary<string, string> DbToAlias)>? expandNavMaps = null;
            if (!string.IsNullOrWhiteSpace(expandRaw))
            {
                // TVF $expand is rejected earlier, in the TVF branch; only Table/View endpoints reach here

                // Nested expand options ($expand=Nav($select=...)) are not supported in v1
                if (expandRaw.Contains('('))
                    return PortwayResults.BadRequest("Nested $expand options are not supported");

                // Root projection must be explicit so the JOIN does not drop the base columns
                if (!odataParams.ContainsKey("select"))
                    return PortwayResults.BadRequest("$expand requires the endpoint to declare AllowedColumns");

                var configured = endpoint.Relationships ?? [];
                var requestedNavs = expandRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var allSqlEndpoints = EndpointHandler.GetSqlEndpoints();
                var caller = context.Features.Get<PortwayApi.Auth.AuthToken>();
                var resolved = new List<(EndpointRelationship Rel, EndpointDefinition Target)>();
                var unknownNavs = new List<string>();
                foreach (var nav in requestedNavs)
                {
                    // unreadable targets return unknown navigation
                    if (configured.FirstOrDefault(r => string.Equals(r.Name, nav, StringComparison.OrdinalIgnoreCase)) is { } rel
                        && ODataToSqlConverter.ResolveRelationshipTarget(rel.Target, allSqlEndpoints) is { Enabled: true } target
                        && target.AllowsEnvironment(env)
                        && caller?.HasAccessToEndpoint(target.Identity) == true)
                        resolved.Add((rel, target));
                    else
                        unknownNavs.Add(nav);
                }
                if (unknownNavs.Count > 0)
                    return PortwayResults.BadRequest($"Unknown $expand navigation(s): {string.Join(", ", unknownNavs)}");

                expandNavMaps = [];
                foreach (var (rel, target) in resolved)
                {
                    if (target.HasTenancy)
                        return PortwayResults.BadRequest($"$expand navigation '{rel.Name}' targets a tenant endpoint, which cannot be expanded");
                    expandNavMaps.Add((rel.Name, target.DatabaseToAlias));
                }

                odataParams["expand"] = string.Join(",", requestedNavs);
            }

            // Step 7: Convert OData to SQL (provider-aware for correct dialect)
            var detectedProviderType = SqlProviderDetector.Detect(connectionString);
            string query;
            Dictionary<string, object> parameters;
            try
            {
                (query, parameters) = tenantPredicates.Count > 0
                    ? _oDataToSqlConverter.ConvertToSQL($"{schema}.{objectName}", odataParams, detectedProviderType, endpoint.Relationships, tenantPredicates)
                    : _oDataToSqlConverter.ConvertToSQL($"{schema}.{objectName}", odataParams, detectedProviderType, endpoint.Relationships);
            }
            catch (Exception odataEx) when (odataEx is not DbException)
            {
                // Malformed OData is a client error, not a server fault
                Log.Warning("Invalid OData query for endpoint {Endpoint}: {Message}", endpointName, odataEx.Message);
                return PortwayResults.BadRequest(InvalidODataMessage);
            }

            object? cachedResponse = null;
            string? cacheKey = null;
            bool cacheEnabled = IsCacheEnabled(endpoint);

            if (cacheEnabled)
            {
                var generation = await GetReadGenerationAsync(endpoint, env);
                cacheKey = BuildCacheKey(env, $"{endpointName}:{generation}", query, parameters, countRequested);
                cachedResponse = await _cacheManager.GetAsync<object>(cacheKey);

                if (cachedResponse != null)
                {
                    Log.Debug("Cache hit for SQL query: {Endpoint}", endpointName);
                    return new OkObjectResult(cachedResponse);
                }
            }

            // Step 9: Execute query
            var aborted = context.RequestAborted;
            await using var connection = _connectionPoolService.CreateConnection(connectionString);
            await connection.OpenAsync(aborted);

            var results = await connection.QueryAsync(new CommandDefinition(query, parameters, cancellationToken: aborted));
            var resultList = results.ToList();

            // Step 10: Transform results to use aliases, but onlyy if column mappings exist
            var transformedResults = resultList;
            if (allowedColumns.Count > 0)
            {
                var databaseToAlias = endpoint.DatabaseToAlias;
                if (databaseToAlias.Count > 0)
                {
                    var aliasResults = ColumnMappingHelper.TransformQueryResultsToAliases(resultList, databaseToAlias);
                    transformedResults = aliasResults.Cast<object>().ToList();
                    Log.Debug("Transformed {Count} results from database columns to aliases", transformedResults.Count);
                }
            }

            // Nest expanded navigation columns (Nav.Column) into a Nav object, aliased to the target's columns
            if (expandNavMaps is { Count: > 0 })
            {
                transformedResults = OdataExpandResponseShaper
                    .Nest(transformedResults, expandNavMaps)
                    .Cast<object>()
                    .ToList();
            }

            // Apply declarative response transforms after alias mapping so rules target alias names
            if (endpoint.ResponseTransforms is { HasRules: true } sqlTransforms)
            {
                transformedResults = HttpResponseTransformHelper.ApplyToRows(transformedResults, sqlTransforms);
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
                    return PortwayResults.NotFound("Record not found");
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
            HttpResponseHeaderHelper.SetPaginationHeaders(context, null, transformedResults.Count, !isLastPage);

            // Only set Cache-Control header if caching is enabled for this endpoint
            if (cacheEnabled)
            {
                var cacheDurationSeconds = GetCacheDurationMinutes(endpoint) * 60;
                HttpResponseHeaderHelper.SetCacheControlHeader(context, cacheDurationSeconds);
            }

            // Run the COUNT query only when the caller asked for it
            long? totalCount = null;
            if (countRequested)
            {
                var (countQuery, countParameters) = tenantPredicates.Count > 0
                    ? _oDataToSqlConverter.ConvertToCountSQL($"{schema}.{objectName}", odataParams, detectedProviderType, tenantPredicates)
                    : _oDataToSqlConverter.ConvertToCountSQL($"{schema}.{objectName}", odataParams, detectedProviderType);
                totalCount = await connection.ExecuteScalarAsync<long>(new CommandDefinition(countQuery, countParameters, cancellationToken: aborted));
            }

            var nextLink = isLastPage ? null : BuildNextLink(env, endpointName, options, top, skip);
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
        catch (Exception) when (context.RequestAborted.IsCancellationRequested)
        {
            // client disconnected, providers report the cancelled command as a DbException
            Log.Debug("SQL query for {EndpointName} cancelled by client disconnect", endpointName);
            return new StatusCodeResult(499);
        }
        catch (DbException dbEx) when (!string.IsNullOrEmpty(id) && SqlErrorClassifier.IsTypeMismatch(dbEx))
        {
            return ClientTypeMismatch(dbEx, endpointName, InvalidRecordIdMessage);
        }
        catch (DbException dbEx)
        {
            return MaskedDataError(context, dbEx, endpointName);
        }
        catch (Exception ex) when (ex is not DbException)
        {
            Log.Error(ex, "Error during SQL query for endpoint: {EndpointName}. Exception Type: {ExceptionType}",
                endpointName, ex.GetType().Name);
            return PortwayResults.ServerError(context, "Error processing. Please check the logs for more details.");
        }
    }

    // json keeps query and parameter values unambiguous and the hash is stable across instances
    /// <summary>
    /// Converts OData aliases to database columns and restricts the projection to AllowedColumns; returns a problem result when refused
    /// </summary>
    private static IActionResult? MapODataColumns(EndpointDefinition endpoint, ref string? select, ref string? filter, ref string? orderby)
    {
        if (endpoint.AllowedColumns is not { Count: > 0 })
            return null;

        var aliasToDatabase = endpoint.AliasToDatabase;

        if (!string.IsNullOrEmpty(select))
        {
            var (isValid, invalidAliases) = ColumnMappingHelper.ValidateAliasColumns(select, aliasToDatabase);
            if (!isValid)
                return PortwayResults.BadRequest($"Selected columns not allowed: {string.Join(", ", invalidAliases)}");

            select = ColumnMappingHelper.ConvertAliasesToDatabaseColumns(select, aliasToDatabase);
        }
        else
        {
            select = string.Join(",", ColumnMappingHelper.GetDatabaseColumns(endpoint.DatabaseToAlias));
        }

        if (!string.IsNullOrEmpty(filter))
            filter = ColumnMappingHelper.ConvertODataFilterAliases(filter, aliasToDatabase);

        if (!string.IsNullOrEmpty(orderby))
            orderby = ColumnMappingHelper.ConvertODataOrderByAliases(orderby, aliasToDatabase);

        return null;
    }

    private static string ReadGenerationKey(EndpointDefinition endpoint, string env) =>
        $"sql:generation:{env}:{endpoint.DatabaseSchema}.{endpoint.DatabaseObjectName}".ToLowerInvariant();

    // new generation hides older entries
    private async Task<string> GetReadGenerationAsync(EndpointDefinition endpoint, string env)
    {
        var key = ReadGenerationKey(endpoint, env);
        var generation = await _cacheManager.GetUncountedAsync<string>(key);
        if (generation is not null)
            return generation;

        generation = Guid.NewGuid().ToString("N");
        await _cacheManager.SetAsync(key, generation, ReadGenerationLifetime);
        return generation;
    }

    private async Task InvalidateReadsAsync(EndpointDefinition endpoint, string env)
    {
        if (IsCacheEnabled(endpoint))
            await _cacheManager.SetAsync(ReadGenerationKey(endpoint, env), Guid.NewGuid().ToString("N"), ReadGenerationLifetime);
    }

    private static readonly TimeSpan ReadGenerationLifetime = TimeSpan.FromDays(7);

    internal static string BuildCacheKey(string env, string endpointName, string query, IReadOnlyDictionary<string, object>? parameters, bool countRequested)
    {
        var material = JsonSerializer.SerializeToUtf8Bytes(new object?[] { query, parameters, countRequested });
        return $"sql:{env}:{endpointName}:{Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(material))}";
    }

    internal const string InvalidRecordIdMessage = "Invalid record ID. Use 123, 'ABC' or guid'3f2a...'.";

    private const string InvalidColumnValueMessage = "Invalid column value. Check the value types.";

    private static IActionResult ClientTypeMismatch(DbException dbEx, string endpointName, string message)
    {
        Log.Warning("Client value does not fit a column type of {EndpointName} ({Detail})", endpointName, SqlErrorClassifier.DescribeForLog(dbEx));
        return PortwayResults.BadRequest(message);
    }

    private static IActionResult DuplicateRecord(DbException dbEx, string endpointName)
    {
        Log.Warning("Duplicate record on {EndpointName} ({Detail})", endpointName, SqlErrorClassifier.DescribeForLog(dbEx));
        return PortwayResults.Conflict("Record already exists");
    }

    private void AddPayloadParameters(DynamicParameters parameters, JsonElement data, string connectionString)
    {
        var provider = _providerFactory.GetProvider(connectionString);
        foreach (var property in data.EnumerateObject())
        {
            var value = GetParameterValue(property.Value);
            parameters.Add($"@{property.Name}", value is string text ? provider.ProcedureValue(text) : value);
        }
    }

    // Masked reference for any provider's database error; full detail stays in the log
    private static IActionResult MaskedDataError(HttpContext context, DbException dbEx, string endpointName)
    {
        var errorId = Guid.NewGuid().ToString("N")[..8];
        Log.Error(dbEx, "Database error [{ErrorId}] for endpoint {EndpointName} ({Detail}): {Message}",
            errorId, endpointName, SqlErrorClassifier.DescribeForLog(dbEx), dbEx.Message);
        return PortwayResults.ServerError(context,
            $"A data error occurred. Please contact support with reference: T{errorId}");
    }

    /// <summary>
    /// Handles SQL POST requests (Create)
    /// </summary>
    public async Task<IActionResult> HandleSqlPostRequest(
        HttpContext context,
        EndpointDefinition endpoint,
        string env,
        string endpointName,
        JsonElement data,
        IReadOnlyDictionary<string, string>? tenants = null)
    {
        var tenantPredicates = TenantSql.Predicates(endpoint, tenants ?? FrozenDictionary<string, string>.Empty);
        if (endpoint.HasTenancy && tenantPredicates.Count == 0)
            return PortwayResults.Forbidden("This endpoint requires tenant access");

        var writeAttempted = false;

        try
        {
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

            if (endpoint.UsesTableWrites)
            {
                if (GuardTableWriteConfig(endpoint, endpointName) is { } configProblem)
                    return configProblem;
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

            if (endpoint.UsesTableWrites)
            {
                writeAttempted = true;
                await using var tableConnection = _connectionPoolService.CreateConnection(connectionString);
                await tableConnection.OpenAsync();
                return await ExecuteTableWriteAsync(tableConnection, connectionString, env, endpoint, endpointName, TableWriteKind.Insert, data, null, tenantPredicates);
            }

            if (endpoint.Procedure is not { Length: > 0 } procedure)
            {
                return PortwayResults.BadRequest("This endpoint does not support insert operations");
            }

            // Prepare stored procedure parameters
            var dynamicParams = new DynamicParameters();
            dynamicParams.Add("@Method", "INSERT");

            if (context.User.Identity?.Name != null)
            {
                dynamicParams.Add("@UserName", context.User.Identity.Name);
            }

            // Extract and add parameters
            AddPayloadParameters(dynamicParams, data, connectionString);

            foreach (var tenant in tenantPredicates)
                dynamicParams.Add($"@{tenant.Column}", tenant.Value);

            // Execute stored procedure
            writeAttempted = true;
            await using var connection = _connectionPoolService.CreateConnection(connectionString);
            await connection.OpenAsync();

            // Intentional user errors (RAISERROR and friends) are caught below
            try
            {
                var result = await ExecuteProcedureAsync(connection, connectionString, procedure, dynamicParams);

                var resultList = result.ToList();

                Log.Debug("Successfully executed INSERT procedure for {Endpoint}", endpointName);

                return PortwayResults.Create($"/api/{env}/{EndpointVersion.RoutePath(endpointName)}", "Record created successfully", resultList.FirstOrDefault());
            }
            catch (DbException dbEx) when (SqlErrorClassifier.IsIntentionalUserError(dbEx))
            {
                Log.Warning("Intentional database error for {Endpoint}: {ErrorMessage}", endpointName, SqlErrorClassifier.GetUserMessage(dbEx));
                return PortwayResults.BadRequest(SqlErrorClassifier.GetUserMessage(dbEx));
            }
        }
        catch (DbException dbEx) when (SqlErrorClassifier.IsDuplicateKey(dbEx))
        {
            return DuplicateRecord(dbEx, endpointName);
        }
        catch (DbException dbEx) when (endpoint.UsesTableWrites && SqlErrorClassifier.IsTypeMismatch(dbEx))
        {
            return ClientTypeMismatch(dbEx, endpointName, InvalidColumnValueMessage);
        }
        catch (DbException dbEx)
        {
            return MaskedDataError(context, dbEx, endpointName);
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
        finally
        {
            if (writeAttempted)
                await InvalidateReadsAsync(endpoint, env);
        }
    }

    /// <summary>
    /// Handles SQL PUT requests (Update)
    /// </summary>
    public async Task<IActionResult> HandleSqlPutRequest(
        HttpContext context,
        EndpointDefinition endpoint,
        string env,
        string endpointName,
        JsonElement data,
        IReadOnlyDictionary<string, string>? tenants = null)
    {
        var tenantPredicates = TenantSql.Predicates(endpoint, tenants ?? FrozenDictionary<string, string>.Empty);
        if (endpoint.HasTenancy && tenantPredicates.Count == 0)
            return PortwayResults.Forbidden("This endpoint requires tenant access");

        var writeAttempted = false;

        try
        {
            // Validate environment
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

            if (endpoint.UsesTableWrites)
            {
                if (GuardTableWriteConfig(endpoint, endpointName) is { } configProblem)
                    return configProblem;
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

            if (endpoint.UsesTableWrites)
            {
                writeAttempted = true;
                await using var tableConnection = _connectionPoolService.CreateConnection(connectionString);
                await tableConnection.OpenAsync();
                return await ExecuteTableWriteAsync(tableConnection, connectionString, env, endpoint, endpointName, TableWriteKind.Update, data, null, tenantPredicates);
            }

            if (endpoint.Procedure is not { Length: > 0 } procedure)
            {
                return PortwayResults.BadRequest("This endpoint does not support update operations");
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
            AddPayloadParameters(dynamicParams, data, connectionString);

            foreach (var tenant in tenantPredicates)
                dynamicParams.Add($"@{tenant.Column}", tenant.Value);

            // Step 8: Execute stored procedure
            writeAttempted = true;
            await using var connection = _connectionPoolService.CreateConnection(connectionString);
            await connection.OpenAsync();

            // Intentional user errors (RAISERROR and friends) are caught below
            try
            {
                var result = await ExecuteProcedureAsync(connection, connectionString, procedure, dynamicParams);

                // Convert result to a list (could be empty if no rows returned)
                var resultList = result.ToList();

                Log.Debug("Successfully executed UPDATE procedure for {Endpoint}", endpointName);

                // Return the results, which typically includes the updated record
                return PortwayResults.Mutation("Record updated successfully", resultList.FirstOrDefault());
            }
            catch (DbException dbEx) when (SqlErrorClassifier.IsIntentionalUserError(dbEx))
            {
                Log.Warning("Intentional database error for {Endpoint}: {ErrorMessage}", endpointName, SqlErrorClassifier.GetUserMessage(dbEx));
                return PortwayResults.BadRequest(SqlErrorClassifier.GetUserMessage(dbEx));
            }
        }
        catch (DbException dbEx) when (SqlErrorClassifier.IsDuplicateKey(dbEx))
        {
            return DuplicateRecord(dbEx, endpointName);
        }
        catch (DbException dbEx) when (endpoint.UsesTableWrites && SqlErrorClassifier.IsTypeMismatch(dbEx))
        {
            return ClientTypeMismatch(dbEx, endpointName, InvalidColumnValueMessage);
        }
        catch (DbException dbEx)
        {
            Log.Error(dbEx, "Database error for {Endpoint} ({Detail}): {ErrorMessage}",
                endpointName, SqlErrorClassifier.DescribeForLog(dbEx), dbEx.Message);
            return PortwayResults.ServerError(context, "Internal operation failed");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error processing UPDATE for {Endpoint}", endpointName);
            return PortwayResults.ServerError(context, "An error occurred while processing your request");
        }
        finally
        {
            if (writeAttempted)
                await InvalidateReadsAsync(endpoint, env);
        }
    }

    /// <summary>
    /// Handles SQL partial updates; method is PATCH or its OData spelling MERGE, which the endpoint must have declared
    /// </summary>
    public async Task<IActionResult> HandleSqlPatchRequest(
        HttpContext context,
        EndpointDefinition endpoint,
        string env,
        string endpointName,
        JsonDocument requestBody,
        string method = "PATCH",
        IReadOnlyDictionary<string, string>? tenants = null)
    {
        var tenantPredicates = TenantSql.Predicates(endpoint, tenants ?? FrozenDictionary<string, string>.Empty);
        if (endpoint.HasTenancy && tenantPredicates.Count == 0)
            return PortwayResults.Forbidden("This endpoint requires tenant access");

        var writeAttempted = false;

        try
        {
            // Validate environment
            var (connectionString, serverName, _) = await _environmentSettingsProvider.LoadEnvironmentOrThrowAsync(env);
            if (string.IsNullOrEmpty(connectionString))
            {
                return PortwayResults.BadRequest($"Invalid or missing environment: {env}");
            }


            if (!(endpoint.Methods?.Contains(method, StringComparer.OrdinalIgnoreCase) ?? false))
            {
                return PortwayResults.MethodNotAllowed();
            }

            if (endpoint.UsesTableWrites)
            {
                if (GuardTableWriteConfig(endpoint, endpointName) is { } configProblem)
                    return configProblem;
            }

            // Step 4: Parse and validate request body
            var data = requestBody.RootElement;

            // Validate against allowed columns, required columns, and regex patterns
            var (isValid, errorMessage, validationErrors) = SqlInputValidator.Validate(data, endpoint, method);
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

            if (endpoint.UsesTableWrites)
            {
                writeAttempted = true;
                await using var tableConnection = _connectionPoolService.CreateConnection(connectionString);
                await tableConnection.OpenAsync();
                return await ExecuteTableWriteAsync(tableConnection, connectionString, env, endpoint, endpointName, TableWriteKind.Update, data, null, tenantPredicates);
            }

            if (endpoint.Procedure is not { Length: > 0 } procedure)
            {
                return PortwayResults.BadRequest("This endpoint does not support partial update operations");
            }

            // Step 6: Prepare stored procedure parameters
            var dynamicParams = new DynamicParameters();

            // Procedures see "PATCH" for both spellings, so MERGE endpoints work against existing procedures
            dynamicParams.Add("@Method", "PATCH");

            // Add user parameter if available
            if (context.User.Identity?.Name != null)
            {
                dynamicParams.Add("@UserName", context.User.Identity.Name);
            }

            // Step 7: Extract and add data parameters from the request. Only the fields provided in the request will be updated
            AddPayloadParameters(dynamicParams, data, connectionString);

            foreach (var tenant in tenantPredicates)
                dynamicParams.Add($"@{tenant.Column}", tenant.Value);

            // Step 8: Execute stored procedure
            writeAttempted = true;
            await using var connection = _connectionPoolService.CreateConnection(connectionString);
            await connection.OpenAsync();

            // Intentional user errors (RAISERROR and friends) are caught below
            try
            {
                var result = await ExecuteProcedureAsync(connection, connectionString, procedure, dynamicParams);

                // Convert result to a list
                var resultList = result.ToList();

                Log.Debug("Successfully executed PATCH procedure for {Endpoint}", endpointName);

                // Return the results
                return PortwayResults.Mutation("Record partially updated successfully", resultList.FirstOrDefault());
            }
            catch (DbException dbEx) when (SqlErrorClassifier.IsIntentionalUserError(dbEx))
            {
                Log.Warning("Intentional database error for {Endpoint}: {ErrorMessage}", endpointName, SqlErrorClassifier.GetUserMessage(dbEx));
                return PortwayResults.BadRequest(SqlErrorClassifier.GetUserMessage(dbEx));
            }
        }
        catch (DbException dbEx) when (SqlErrorClassifier.IsDuplicateKey(dbEx))
        {
            return DuplicateRecord(dbEx, endpointName);
        }
        catch (DbException dbEx) when (endpoint.UsesTableWrites && SqlErrorClassifier.IsTypeMismatch(dbEx))
        {
            return ClientTypeMismatch(dbEx, endpointName, InvalidColumnValueMessage);
        }
        catch (DbException dbEx)
        {
            Log.Error(dbEx, "Database error for {Endpoint} ({Detail}): {ErrorMessage}",
                endpointName, SqlErrorClassifier.DescribeForLog(dbEx), dbEx.Message);
            return PortwayResults.ServerError(context, "Internal operation failed");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error processing PATCH for {Endpoint}", endpointName);
            return PortwayResults.ServerError(context, "An error occurred while processing your request");
        }
        finally
        {
            if (writeAttempted)
                await InvalidateReadsAsync(endpoint, env);
        }
    }

    /// <summary>
    /// Handles SQL DELETE requests
    /// </summary>
    public async Task<IActionResult> HandleSqlDeleteRequest(
        HttpContext context,
        EndpointDefinition endpoint,
        string env,
        string endpointName,
        string id,
        IReadOnlyDictionary<string, string>? tenants = null)
    {
        var tenantPredicates = TenantSql.Predicates(endpoint, tenants ?? FrozenDictionary<string, string>.Empty);
        if (endpoint.HasTenancy && tenantPredicates.Count == 0)
            return PortwayResults.Forbidden("This endpoint requires tenant access");

        var writeAttempted = false;

        try
        {
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

            if (endpoint.UsesTableWrites)
            {
                if (GuardTableWriteConfig(endpoint, endpointName) is { } configProblem)
                    return configProblem;
            }

            // Check if the ID is provided
            if (string.IsNullOrEmpty(id))
            {
                return PortwayResults.BadRequest("ID parameter is required for delete operations");
            }

            if (endpoint.UsesTableWrites)
            {
                writeAttempted = true;
                await using var tableConnection = _connectionPoolService.CreateConnection(connectionString);
                await tableConnection.OpenAsync();
                return await ExecuteTableWriteAsync(tableConnection, connectionString, env, endpoint, endpointName, TableWriteKind.Delete, null, id, tenantPredicates);
            }

            if (endpoint.Procedure is not { Length: > 0 } procedure)
            {
                return PortwayResults.BadRequest("This endpoint does not support delete operations");
            }

            // Prepare stored procedure parameters
            var dynamicParams = new DynamicParameters();

            // Add method parameter (always needed for the standard procedure pattern)
            dynamicParams.Add("@Method", "DELETE");

            // Handle different primary key parameter names
            var primaryKey = endpoint.PrimaryKey ?? "Id";
            var keyValue = _providerFactory.GetProvider(connectionString).ProcedureValue(id);
            dynamicParams.Add($"@{primaryKey}", keyValue);

            // For backward compatibility, also add @id parameter
            if (!primaryKey.Equals("id", StringComparison.OrdinalIgnoreCase))
            {
                dynamicParams.Add("@id", keyValue);
            }

            // Add user parameter if available
            if (context.User.Identity?.Name != null)
            {
                dynamicParams.Add("@UserName", context.User.Identity.Name);
            }

            foreach (var tenant in tenantPredicates)
                dynamicParams.Add($"@{tenant.Column}", tenant.Value);

            // Execute stored procedure
            writeAttempted = true;
            await using var connection = _connectionPoolService.CreateConnection(connectionString);
            await connection.OpenAsync();

            // Intentional user errors (RAISERROR and friends) are caught below
            try
            {
                var result = await ExecuteProcedureAsync(connection, connectionString, procedure, dynamicParams);

                // Convert result to a list (could be empty if no rows returned)
                var resultList = result.ToList();

                Log.Debug("Successfully executed DELETE procedure for {Endpoint}", endpointName);

                // Return the results, which typically includes deletion confirmation
                return PortwayResults.Mutation("Record deleted successfully", resultList.FirstOrDefault());
            }
            catch (DbException dbEx) when (SqlErrorClassifier.IsIntentionalUserError(dbEx))
            {
                Log.Warning("Intentional database error for {Endpoint}: {ErrorMessage}", endpointName, SqlErrorClassifier.GetUserMessage(dbEx));
                return PortwayResults.BadRequest(SqlErrorClassifier.GetUserMessage(dbEx));
            }
        }
        catch (DbException dbEx) when (endpoint.UsesTableWrites && SqlErrorClassifier.IsTypeMismatch(dbEx))
        {
            return ClientTypeMismatch(dbEx, endpointName, InvalidRecordIdMessage);
        }
        catch (DbException dbEx)
        {
            Log.Error(dbEx, "Database error for {Endpoint} ({Detail}): {ErrorMessage}",
                endpointName, SqlErrorClassifier.DescribeForLog(dbEx), dbEx.Message);
            return PortwayResults.ServerError(context, "Internal operation failed");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error processing DELETE for {Endpoint}", endpointName);
            return PortwayResults.ServerError(context, "An error occurred while processing your request");
        }
        finally
        {
            if (writeAttempted)
                await InvalidateReadsAsync(endpoint, env);
        }
    }


    private static readonly string[] IdFieldNames = new[]
    {
        "id", "Id", "ID", "IdField", "IDField",
        "pk", "PK", "PrimaryKey", "primaryKey", "primarykey",
        "internalId", "InternalId", "InternalID", "internalid",
        "recordId", "RecordId"
    };

    /// <summary>
    /// Builds the OData $skip/$top next-page link
    /// </summary>
    private static string BuildNextLink(
        string env,
        string endpointPath,
        ODataReadOptions options,
        int top,
        int skip,
        string[]? pathSegments = null)
    {
        var path = string.Concat((pathSegments ?? []).Select(segment => "/" + Uri.EscapeDataString(segment)));
        var nextLink = $"/api/{env}/{EndpointVersion.RoutePath(endpointPath)}{path}?$top={top}&$skip={skip + top}";

        if (!string.IsNullOrWhiteSpace(options.Select))
            nextLink += $"&$select={Uri.EscapeDataString(options.Select)}";

        if (!string.IsNullOrWhiteSpace(options.Filter))
            nextLink += $"&$filter={Uri.EscapeDataString(options.Filter)}";

        if (!string.IsNullOrWhiteSpace(options.OrderBy))
            nextLink += $"&$orderby={Uri.EscapeDataString(options.OrderBy)}";

        if (!string.IsNullOrWhiteSpace(options.Expand))
            nextLink += $"&$expand={Uri.EscapeDataString(options.Expand)}";

        if (options.Count)
            nextLink += "&$count=true";

        return nextLink;
    }

    /// <summary>
    /// Converts a JsonElement to its native CLR parameter value
    /// </summary>
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

    /// <summary>
    /// Parses a stored procedure name into schema and procedure name parts. Handles both "schema.procedure" and plain "procedure" formats, stripping brackets
    /// </summary>
    private static (string Schema, string ProcedureName) ParseProcedureName(string procedure, ISqlProvider provider, DbConnection connection)
    {
        if (procedure.Contains('.'))
        {
            var parts = procedure.Split('.');
            return (SqlSchemaResolver.Resolve(parts[0].Trim('[', ']'), provider, connection.Database), parts[1].Trim('[', ']'));
        }
        return (SqlSchemaResolver.Resolve(null, provider, connection.Database), procedure);
    }

    /// <summary>
    /// Applies the MaxPageSize limit from endpoint properties, overriding the requested top value if necessary
    /// </summary>
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

    /// <summary>
    /// Applies the DefaultSort from endpoint properties if no orderby is provided
    /// </summary>
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

    /// <summary>
    /// Checks if caching is enabled for the endpoint
    /// </summary>
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

        // Defer to the global setting, CacheManager enforces Enabled=false
        return true;
    }

    /// <summary>
    /// Gets the cache duration in minutes for the endpoint
    /// </summary>
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

    /// <summary>
    /// Validates SQL parameters for update and delete operations
    /// </summary>
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
