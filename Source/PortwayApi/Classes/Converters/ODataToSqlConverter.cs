using PortwayApi.Services.Providers;
using PortwayApi.Interfaces;
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
        => Convert(entityName, odataParams, providerType, count: false);

    public (string SqlQuery, Dictionary<string, object> Parameters) ConvertToCountSQL(
        string entityName,
        Dictionary<string, string> odataParams,
        SqlProviderType providerType)
    {
        // Count ignores paging, projection and ordering; only the filter shapes the result
        var countParams = new Dictionary<string, string>();
        if (odataParams.TryGetValue("filter", out var filter) && !string.IsNullOrWhiteSpace(filter))
            countParams["filter"] = filter;
        return Convert(entityName, countParams, providerType, count: true);
    }

    private (string SqlQuery, Dictionary<string, object> Parameters) Convert(
        string entityName,
        Dictionary<string, string> odataParams,
        SqlProviderType providerType,
        bool count)
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

        try
        {
            if (odataParams.TryGetValue("select", out var select) && !string.IsNullOrWhiteSpace(select))
                Log.Debug("Applied $select: {Columns}", select);
            if (odataParams.TryGetValue("filter", out var filter) && !string.IsNullOrWhiteSpace(filter))
                Log.Debug("Applied $filter: {Filter}", filter);
            if (odataParams.TryGetValue("orderby", out var orderby) && !string.IsNullOrWhiteSpace(orderby))
                Log.Debug("Applied $orderby: {OrderBy}", orderby);
            if (odataParams.TryGetValue("top", out var topStr) && int.TryParse(topStr, out var top))
                Log.Debug("Applied $top: {Top}", top);
            if (odataParams.TryGetValue("skip", out var skipStr) && int.TryParse(skipStr, out var skip))
                Log.Debug("Applied $skip: {Skip}", skip);

            var (sqlQuery, rawParams) = dynamicConverter.ConvertToSQL(
                fullTableName,
                odataParams,
                count,
                true
            );

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
}
