using PortwayApi.Interfaces;
using SqlKata.Compilers;
using Serilog;

namespace PortwayApi.Classes;

/// <summary>
/// Implements the IODataToSqlConverter interface to convert OData queries to SQL
/// </summary>
public class ODataToSqlConverter : IODataToSqlConverter
{
    private readonly IEdmModelBuilder _edmModelBuilder;
    private readonly Compiler _sqlCompiler;
    private readonly DynamicODataToSQL.ODataToSqlConverter _dynamicConverter;
    
    public ODataToSqlConverter(IEdmModelBuilder edmModelBuilder, Compiler sqlCompiler)
    {
        _edmModelBuilder = edmModelBuilder;
        _sqlCompiler = sqlCompiler;
        
        // Initialize the DynamicODataToSQL converter
        var dynamicEdmModelBuilder = new DynamicODataToSQL.EdmModelBuilder();
        _dynamicConverter = new DynamicODataToSQL.ODataToSqlConverter(dynamicEdmModelBuilder, sqlCompiler);
    }
    
    public (string SqlQuery, Dictionary<string, object> Parameters) ConvertToSQL(
        string entityName, 
        Dictionary<string, string> odataParams)
    {
        Log.Debug("🔄 Converting OData to SQL for entity: {EntityName}", entityName);
        
        // Get the endpoint definition to retrieve schema and table info
        var sqlEndpoints = EndpointHandler.GetSqlEndpoints();
        string schema = "dbo"; // Default schema
        string tableName = entityName;
        
        // Check if we can get the actual schema from the endpoint definition
        if (sqlEndpoints.TryGetValue(entityName, out var endpoint))
        {
            schema = endpoint.DatabaseSchema ?? "dbo";
            tableName = endpoint.DatabaseObjectName ?? entityName;
            Log.Debug("📊 Found endpoint definition: Schema={Schema}, Table={Table}", schema, tableName);
        }
        else
        {
            // Fallback to parsing from entityName if not found in endpoints
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
            Log.Debug("⚠️ No endpoint definition found, using parsed values: Schema={Schema}, Table={Table}", schema, tableName);
        }
        
        // Let the library handle the bracketing - just provide clean schema.table format
        string fullTableName = $"{schema}.{tableName}";
        
        try
        {
            // Log the OData parameters for debugging
            if (odataParams.TryGetValue("select", out var select) && !string.IsNullOrWhiteSpace(select))
            {
                Log.Debug("🔍 Applied $select: {Columns}", select);
            }
            
            if (odataParams.TryGetValue("filter", out var filter) && !string.IsNullOrWhiteSpace(filter))
            {
                Log.Debug("🔍 Applied $filter: {Filter}", filter);
            }
            
            if (odataParams.TryGetValue("orderby", out var orderby) && !string.IsNullOrWhiteSpace(orderby))
            {
                Log.Debug("🔍 Applied $orderby: {OrderBy}", orderby);
            }
            
            if (odataParams.TryGetValue("top", out var topStr) && int.TryParse(topStr, out var top))
            {
                Log.Debug("🔍 Applied $top: {Top}", top);
            }
            
            if (odataParams.TryGetValue("skip", out var skipStr) && int.TryParse(skipStr, out var skip))
            {
                Log.Debug("🔍 Applied $skip: {Skip}", skip);
            }
            
            // Use DynamicODataToSQL to convert the query
            var (sqlQuery, rawParams) = _dynamicConverter.ConvertToSQL(
                fullTableName,
                odataParams,
                false, // count parameter
                true   // tryToParseDate - enable date parsing
            );
            
            // Ensure parameters are a Dictionary (not just IDictionary)
            var parameters = new Dictionary<string, object>(rawParams ?? new Dictionary<string, object>());
            
            Log.Debug("✅ Successfully converted OData to SQL");
            Log.Debug("SQL Query: {SqlQuery}", sqlQuery);
            
            if (parameters.Any())
            {
                Log.Debug("Parameters: {Parameters}", 
                    string.Join(", ", parameters.Select(p => $"{p.Key}={p.Value}")));
            }
            
            return (sqlQuery, parameters);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ Error converting OData to SQL: {Message}", ex.Message);
            throw new InvalidOperationException($"Failed to convert OData to SQL: {ex.Message}", ex);
        }
    }
}