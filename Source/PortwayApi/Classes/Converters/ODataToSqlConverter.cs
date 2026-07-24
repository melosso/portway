using PortwayApi.Services.Providers;
using PortwayApi.Interfaces;
using SqlKata;
using SqlKata.Compilers;
using Serilog;

namespace PortwayApi.Classes;

/// <summary>Implements IODataToSqlConverter, routing OData queries to the correct SQL dialect based on the connection string provider type</summary>
public class ODataToSqlConverter : IODataToSqlConverter
{
    private readonly IEdmModelBuilder _edmModelBuilder;
    private readonly IReadOnlyDictionary<SqlProviderType, Compiler> _compilers;
    private readonly IReadOnlyDictionary<SqlProviderType, ISqlProvider> _providers;

    public ODataToSqlConverter(IEdmModelBuilder edmModelBuilder, IEnumerable<ISqlProvider> providers)
    {
        _edmModelBuilder = edmModelBuilder;
        _providers = providers.ToDictionary(p => p.ProviderType);
        _compilers = _providers.ToDictionary(p => p.Key, p => p.Value.GetCompiler());
    }

    public (string SqlQuery, Dictionary<string, object> Parameters) ConvertToSQL(
        string entityName,
        Dictionary<string, string> odataParams)
        => ConvertToSQL(entityName, odataParams, SqlProviderType.SqlServer);

    public (string SqlQuery, Dictionary<string, object> Parameters) ConvertToSQL(
        string entityName,
        Dictionary<string, string> odataParams,
        SqlProviderType providerType)
        => Convert(entityName, odataParams, providerType, count: false, relationships: null);

    public (string SqlQuery, Dictionary<string, object> Parameters) ConvertToSQL(
        string entityName,
        Dictionary<string, string> odataParams,
        SqlProviderType providerType,
        IReadOnlyList<EndpointRelationship>? relationships)
        => Convert(entityName, odataParams, providerType, count: false, relationships);

    public (string SqlQuery, Dictionary<string, object> Parameters) ConvertToCountSQL(
        string entityName,
        Dictionary<string, string> odataParams,
        SqlProviderType providerType)
    {
        // Count ignores paging, projection and ordering; only the filter shapes the result
        var countParams = new Dictionary<string, string>();
        if (odataParams.TryGetValue("filter", out var filter) && !string.IsNullOrWhiteSpace(filter))
            countParams["filter"] = filter;
        return Convert(entityName, countParams, providerType, count: true, relationships: null);
    }

    private (string SqlQuery, Dictionary<string, object> Parameters) Convert(
        string entityName,
        Dictionary<string, string> odataParams,
        SqlProviderType providerType,
        bool count,
        IReadOnlyList<EndpointRelationship>? relationships)
    {
        Log.Debug("Converting OData to SQL for entity: {EntityName} (provider: {Provider})", entityName, providerType);

        var sqlEndpoints = EndpointHandler.GetSqlEndpoints();
        string schema = "dbo";
        string tableName = entityName;

        if (sqlEndpoints.TryGetValue(entityName, out var endpoint))
        {
            schema = endpoint.DatabaseSchema ?? "dbo";
            tableName = endpoint.DatabaseObjectName ?? entityName;
            Log.Debug("Found endpoint definition: Schema={Schema}, Table={Table}", schema, tableName);
        }
        else
        {
            string CleanName(string name) => name.Replace("[", "").Replace("]", "");

            if (entityName.Contains("."))
            {
                var parts = entityName.Split('.');
                schema = CleanName(parts[0]);
                tableName = CleanName(parts[1]);
            }
            else
            {
                tableName = CleanName(entityName);
            }

            Log.Debug("No endpoint definition found, using parsed values: Schema={Schema}, Table={Table}", schema, tableName);
        }

        // Fail closed: wrong-dialect SQL for a known connection is a correctness trap
        if (!_compilers.TryGetValue(providerType, out var compiler) ||
            !_providers.TryGetValue(providerType, out var provider))
            throw new InvalidOperationException($"No SQL compiler registered for provider '{providerType}'. Check the provider registration in AddPortwaySqlServices.");

        // Empty resolved schema means unqualified (SQLite, or MySQL scoping by connection database)
        var resolvedSchema = PortwayApi.Helpers.SqlSchemaResolver.Resolve(schema, provider);
        string fullTableName = resolvedSchema.Length > 0 ? $"{resolvedSchema}.{tableName}" : tableName;

        var dynamicEdmModelBuilder = new DynamicODataToSQL.EdmModelBuilder();
        var dynamicConverter = new DynamicODataToSQL.ODataToSqlConverter(dynamicEdmModelBuilder, compiler);

        // $expand is applied as manual SqlKata JOINs on the fork's open model. Declaring the FK as a
        // typed EDM property would make the OData binder reject filters like "Fk eq 10" (typed vs literal),
        // so the base query stays fully open and the JOIN is added after the fact
        bool expandRequested = odataParams.TryGetValue("expand", out var expandValue) && !string.IsNullOrWhiteSpace(expandValue);
        List<RelationalExpandSpec>? expandSpecs = expandRequested && relationships is { Count: > 0 } && !count
            ? BuildExpandSpecs(expandValue!, relationships, provider, sqlEndpoints)
            : null;

        // The fork never sees "expand"; navigation joins are added by hand below
        var forkParams = odataParams;
        if (odataParams.ContainsKey("expand"))
        {
            forkParams = new Dictionary<string, string>(odataParams);
            forkParams.Remove("expand");
        }

        try
        {
            if (forkParams.TryGetValue("select", out var select) && !string.IsNullOrWhiteSpace(select))
                Log.Debug("Applied $select: {Columns}", select);
            if (forkParams.TryGetValue("filter", out var filter) && !string.IsNullOrWhiteSpace(filter))
                Log.Debug("Applied $filter: {Filter}", filter);
            if (forkParams.TryGetValue("orderby", out var orderby) && !string.IsNullOrWhiteSpace(orderby))
                Log.Debug("Applied $orderby: {OrderBy}", orderby);

            string sqlQuery;
            IDictionary<string, object> rawParams;

            if (expandSpecs is { Count: > 0 })
            {
                var kata = dynamicConverter.ConvertToSQLKataQuery(fullTableName, forkParams, count, true);
                kata = PortwayApi.Helpers.OdataExpandJoinBuilder.Apply(kata, fullTableName, expandSpecs);
                var compiled = compiler.Compile(kata);
                (sqlQuery, rawParams) = (compiled.Sql, compiled.NamedBindings);
            }
            else
            {
                (sqlQuery, rawParams) = dynamicConverter.ConvertToSQL(fullTableName, forkParams, count, true);
            }

            var parameters = new Dictionary<string, object>(rawParams ?? new Dictionary<string, object>());

            Log.Debug("Successfully converted OData to SQL");
            Log.Debug("SQL Query: {SqlQuery}", sqlQuery);

            if (parameters.Any())
                Log.Debug("Parameters: {Parameters}", string.Join(", ", parameters.Select(p => $"{p.Key}={p.Value}")));

            return (sqlQuery, parameters);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error converting OData to SQL: {Message}", ex.Message);
            throw new InvalidOperationException($"Failed to convert OData to SQL: {ex.Message}", ex);
        }
    }

    /// <summary>Resolves the requested navigation names to EDM emission specs using the target endpoints' own schema/table/columns</summary>
    private static List<RelationalExpandSpec> BuildExpandSpecs(
        string expandValue,
        IReadOnlyList<EndpointRelationship> relationships,
        ISqlProvider provider,
        Dictionary<string, EndpointDefinition> sqlEndpoints)
    {
        var specs = new List<RelationalExpandSpec>();

        // Bare navigation names only; nested options are rejected upstream on the read path
        var requested = expandValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var navRaw in requested)
        {
            var nav = navRaw.Contains('(') ? navRaw[..navRaw.IndexOf('(')].Trim() : navRaw;

            var rel = relationships.FirstOrDefault(r => string.Equals(r.Name, nav, StringComparison.OrdinalIgnoreCase));
            if (rel == null)
                throw new InvalidOperationException($"Unknown navigation '{nav}' for $expand");

            if (!TryResolveTarget(rel.Target, sqlEndpoints, out var target) || target == null)
                throw new InvalidOperationException($"Relationship '{rel.Name}' targets unregistered endpoint '{rel.Target}'");

            var targetSchema = PortwayApi.Helpers.SqlSchemaResolver.Resolve(target.DatabaseSchema ?? "dbo", provider);
            var targetTable = targetSchema.Length > 0
                ? $"{targetSchema}.{target.DatabaseObjectName}"
                : target.DatabaseObjectName ?? rel.Target;

            var targetColumns = target.DatabaseToAlias.Keys.ToList();

            specs.Add(new RelationalExpandSpec(rel.Name, targetTable, rel.LocalColumn, rel.TargetColumn, targetColumns));
        }

        return specs;
    }

    /// <summary>Target-by-name resolution: exact endpoint key or a namespaced key ending in the plain name</summary>
    private static bool TryResolveTarget(string target, Dictionary<string, EndpointDefinition> sqlEndpoints, out EndpointDefinition? endpoint)
    {
        if (sqlEndpoints.TryGetValue(target, out endpoint))
            return true;

        var key = sqlEndpoints.Keys.FirstOrDefault(k => k.EndsWith($"/{target}", StringComparison.OrdinalIgnoreCase));
        if (key != null)
        {
            endpoint = sqlEndpoints[key];
            return true;
        }

        endpoint = null;
        return false;
    }
}
