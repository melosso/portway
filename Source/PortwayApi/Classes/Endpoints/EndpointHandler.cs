namespace PortwayApi.Classes;

using System.Text.Json;
using Serilog;

/// <summary>
/// Unified endpoint definition that handles all endpoint types
/// </summary>
public class EndpointDefinition
{
    public string Url { get; set; } = string.Empty;
    public List<string> Methods { get; set; } = new List<string>();
    public EndpointType Type { get; set; } = EndpointType.Standard;
    public CompositeDefinition? CompositeConfig { get; set; }
    public bool IsPrivate { get; set; } = false;
    
    // SQL endpoint properties
    public string? DatabaseObjectName { get; set; }
    public string? DatabaseSchema { get; set; }
    public List<string>? AllowedColumns { get; set; }
    public string? Procedure { get; set; }
    public string? PrimaryKey { get; set; }
    
    // Column mapping properties (lazy-loaded from AllowedColumns)
    private Dictionary<string, string>? _aliasToDatabase;
    private Dictionary<string, string>? _databaseToAlias;
    
    public Dictionary<string, string> AliasToDatabase
    {
        get
        {
            if (_aliasToDatabase == null)
            {
                var (aliasToDb, dbToAlias) = PortwayApi.Classes.Helpers.ColumnMappingHelper.ParseColumnMappings(AllowedColumns);
                _aliasToDatabase = aliasToDb;
                _databaseToAlias = dbToAlias;
            }
            return _aliasToDatabase;
        }
    }
    
    public Dictionary<string, string> DatabaseToAlias
    {
        get
        {
            if (_databaseToAlias == null)
            {
                var (aliasToDb, dbToAlias) = PortwayApi.Classes.Helpers.ColumnMappingHelper.ParseColumnMappings(AllowedColumns);
                _aliasToDatabase = aliasToDb;
                _databaseToAlias = dbToAlias;
            }
            return _databaseToAlias;
        }
    }
    
    // Environment restrictions
    public List<string>? AllowedEnvironments { get; set; }

    // File endpoint properties (optional, only for file endpoints)
    public Dictionary<string, object>? Properties { get; set; }
    
    // OpenAPI documentation properties
    public Documentation? Documentation { get; set; } // OpenAPI documentation settings

    // Helper properties to simplify type checking
    public bool IsStandard => Type == EndpointType.Standard && !IsPrivate;
    public bool IsComposite => Type == EndpointType.Composite || 
                              (CompositeConfig != null && !string.IsNullOrEmpty(CompositeConfig.Name));
    public bool IsSql => Type == EndpointType.SQL;
    public bool IsStatic => Type == EndpointType.Static;
                              
    // Helper method to get a consistent tuple format compatible with existing code
    public (string Url, HashSet<string> Methods, bool IsPrivate, string Type) ToTuple()
    {
        string typeString = this.Type.ToString();
        return (Url, new HashSet<string>(Methods, StringComparer.OrdinalIgnoreCase), IsPrivate, typeString);
    }
}

public static class EndpointHandler
{
    // Cache for loaded endpoints to avoid multiple loads
    private static Dictionary<string, EndpointDefinition>? _loadedProxyEndpoints = null;
    private static Dictionary<string, EndpointDefinition>? _loadedSqlEndpoints = null;
    private static Dictionary<string, EndpointDefinition>? _loadedSqlWebhookEndpoints = null;
    private static Dictionary<string, EndpointDefinition>? _loadedFileEndpoints = null;
    private static Dictionary<string, EndpointDefinition>? _loadedStaticEndpoints = null;
    private static readonly object _loadLock = new object();

    /// <summary>
    /// Gets SQL endpoints from the /endpoints/SQL directory
    /// </summary>
    public static Dictionary<string, EndpointDefinition> GetSqlEndpoints()
    {
        string sqlEndpointsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "endpoints", "SQL");
        LoadSqlEndpointsIfNeeded(sqlEndpointsDirectory);
        return _loadedSqlEndpoints!;
    }

    /// <summary>
    /// Gets SQL webhook endpoints from the /endpoints/Webhooks directory
    /// </summary>
    public static Dictionary<string, EndpointDefinition> GetSqlWebhookEndpoints()
    {
        string sqlWebhookEndpointsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "endpoints", "Webhooks");
        LoadSqlWebhookEndpointsIfNeeded(sqlWebhookEndpointsDirectory);
        return _loadedSqlWebhookEndpoints!;
    }

    /// <summary>
    /// Gets Proxy endpoints from the /endpoints/Proxy directory
    /// </summary>
    public static Dictionary<string, EndpointDefinition> GetProxyEndpoints()
    {
        string proxyEndpointsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "endpoints", "Proxy");
        LoadProxyEndpointsIfNeeded(proxyEndpointsDirectory);
        return _loadedProxyEndpoints!;
    }

    /// <summary>
    /// Gets all composite endpoint definitions from the endpoints directory
    /// </summary>
    public static Dictionary<string, CompositeDefinition> GetCompositeDefinitions(Dictionary<string, (string Url, HashSet<string> Methods, bool IsPrivate, string Type)> endpointMap)
    {
        // We already have endpoints loaded, so just extract the composite configs
        var compositeDefinitions = new Dictionary<string, CompositeDefinition>(StringComparer.OrdinalIgnoreCase);

        // If proxy endpoints haven't been loaded yet, load them
        if (_loadedProxyEndpoints == null)
        {
            string proxyEndpointsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "endpoints", "Proxy");
            LoadProxyEndpointsIfNeeded(proxyEndpointsDirectory);
        }

        foreach (var kvp in _loadedProxyEndpoints!)
        {
            if (kvp.Value.IsComposite && kvp.Value.CompositeConfig != null)
            {
                compositeDefinitions[kvp.Key] = kvp.Value.CompositeConfig;
            }
        }

        return compositeDefinitions;
    }

    /// <summary>
    /// Loads file endpoints if they haven't been loaded yet
    /// </summary>
    private static void LoadFileEndpointsIfNeeded(string endpointsDirectory)
    {
        // Use double-check locking pattern to ensure thread safety
        if (_loadedFileEndpoints == null)
        {
            lock (_loadLock)
            {
                if (_loadedFileEndpoints == null)
                {
                    _loadedFileEndpoints = LoadFileEndpoints(endpointsDirectory);
                }
            }
        }
    }

    /// <summary>
    /// Loads static endpoints if they haven't been loaded yet
    /// </summary>
    private static void LoadStaticEndpointsIfNeeded(string endpointsDirectory)
    {
        // Use double-check locking pattern to ensure thread safety
        if (_loadedStaticEndpoints == null)
        {
            lock (_loadLock)
            {
                if (_loadedStaticEndpoints == null)
                {
                    _loadedStaticEndpoints = LoadStaticEndpoints(endpointsDirectory);
                }
            }
        }
    }

    /// <summary>
    /// Scans the specified directory for endpoint definition files and returns a dictionary of endpoints.
    /// </summary>
    /// <param name="endpointsDirectory">Directory containing endpoint definitions</param>
    /// <returns>Dictionary with endpoint names as keys and tuples of (url, methods, isPrivate, type) as values</returns>
    public static Dictionary<string, (string Url, HashSet<string> Methods, bool IsPrivate, string Type)> GetEndpoints(string endpointsDirectory)
    {
        // Check if the directory is for proxy or SQL endpoints
        bool isProxyEndpoint = endpointsDirectory.Contains("Proxy", StringComparison.OrdinalIgnoreCase);

        // Load endpoints if not already loaded
        if (isProxyEndpoint)
        {
            LoadProxyEndpointsIfNeeded(endpointsDirectory);

            // Convert to the legacy format
            var endpointMap = new Dictionary<string, (string Url, HashSet<string> Methods, bool IsPrivate, string Type)>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in _loadedProxyEndpoints!)
            {
                endpointMap[kvp.Key] = kvp.Value.ToTuple();
            }

            return endpointMap;
        }
        else
        {
            // Create an empty dictionary for now - SQL endpoints are handled differently
            return new Dictionary<string, (string Url, HashSet<string> Methods, bool IsPrivate, string Type)>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Internal method to load proxy endpoints if they haven't been loaded yet
    /// </summary>
    private static void LoadProxyEndpointsIfNeeded(string endpointsDirectory)
    {
        // Use double-check locking pattern to ensure thread safety
        if (_loadedProxyEndpoints == null)
        {
            lock (_loadLock)
            {
                if (_loadedProxyEndpoints == null)
                {
                    _loadedProxyEndpoints = LoadProxyEndpoints(endpointsDirectory);
                }
            }
        }
    }

    /// <summary>
    /// Internal method to load SQL endpoints if they haven't been loaded yet
    /// </summary>
    private static void LoadSqlEndpointsIfNeeded(string endpointsDirectory)
    {
        // Use double-check locking pattern to ensure thread safety
        if (_loadedSqlEndpoints == null)
        {
            lock (_loadLock)
            {
                if (_loadedSqlEndpoints == null)
                {
                    _loadedSqlEndpoints = LoadSqlEndpoints(endpointsDirectory);
                }
            }
        }
    }

    /// <summary>
    /// Internal method to load SQL endpoints if they haven't been loaded yet
    /// </summary>
    private static void LoadSqlWebhookEndpointsIfNeeded(string endpointsDirectory)
    {
        // Use double-check locking pattern to ensure thread safety
        if (_loadedSqlWebhookEndpoints == null)
        {
            lock (_loadLock)
            {
                if (_loadedSqlWebhookEndpoints == null)
                {
                    _loadedSqlWebhookEndpoints = LoadSqlWebhookEndpoints(endpointsDirectory);
                }
            }
        }
    }

    /// <summary>
    /// Internal method to load all proxy endpoints from the endpoints directory
    /// </summary>
    private static Dictionary<string, EndpointDefinition> LoadProxyEndpoints(string endpointsDirectory)
    {
        var endpoints = new Dictionary<string, EndpointDefinition>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (!Directory.Exists(endpointsDirectory))
            {
                Log.Warning($"⚠️ Proxy endpoints directory not found: {endpointsDirectory}");
                Directory.CreateDirectory(endpointsDirectory);
                return endpoints;
            }

            // Get all JSON files in the endpoints directory and subdirectories
            foreach (var file in Directory.GetFiles(endpointsDirectory, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    // Read and parse the endpoint definition
                    var json = File.ReadAllText(file);
                    var definition = ParseProxyEndpointDefinition(json);

                    if (definition != null && !string.IsNullOrWhiteSpace(definition.Url) && definition.Methods.Any())
                    {
                        // Extract endpoint name from directory name
                        var endpointName = Path.GetFileName(Path.GetDirectoryName(file)) ?? "";

                        // Skip if no valid name could be extracted
                        if (string.IsNullOrWhiteSpace(endpointName))
                        {
                            Log.Warning("⚠️ Could not determine endpoint name for {File}", file);
                            continue;
                        }

                        // Add the endpoint to the dictionary
                        endpoints[endpointName] = definition;

                        LogEndpointLoading(endpointName, definition);
                    }
                    else
                    {
                        Log.Warning("⚠️ Failed to load endpoint from {File}", file);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "❌ Error parsing endpoint file: {File}", file);
                }
            }

            Log.Debug($"✅ Loaded {endpoints.Count} proxy endpoints from {endpointsDirectory}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ Error scanning proxy endpoints directory: {Directory}", endpointsDirectory);
        }

        return endpoints;
    }

    /// <summary>
    /// Internal method to load all SQL endpoints from the endpoints directory
    /// </summary>
    private static Dictionary<string, EndpointDefinition> LoadSqlEndpoints(string endpointsDirectory)
    {
        var endpoints = new Dictionary<string, EndpointDefinition>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (!Directory.Exists(endpointsDirectory))
            {
                Log.Warning($"⚠️ SQL endpoints directory not found: {endpointsDirectory}");
                Directory.CreateDirectory(endpointsDirectory);
                return endpoints;
            }

            // Get all JSON files in the endpoints directory and subdirectories
            foreach (var file in Directory.GetFiles(endpointsDirectory, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    // Read and parse the endpoint definition
                    var json = File.ReadAllText(file);
                    var definition = ParseSqlEndpointDefinition(json);

                    if (definition != null && !string.IsNullOrWhiteSpace(definition.DatabaseObjectName))
                    {
                        // Extract endpoint name from directory name
                        var endpointName = Path.GetFileName(Path.GetDirectoryName(file)) ?? "";

                        // Skip if no valid name could be extracted
                        if (string.IsNullOrWhiteSpace(endpointName))
                        {
                            Log.Warning("⚠️ Could not determine endpoint name for {File}", file);
                            continue;
                        }

                        // Add the endpoint to the dictionary
                        endpoints[endpointName] = definition;

                        Log.Debug($"📊 SQL Endpoint: {endpointName}; Object: {definition.DatabaseSchema}.{definition.DatabaseObjectName}");
                    }
                    else
                    {
                        Log.Warning("⚠️ Failed to load SQL endpoint from {File}", file);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "❌ Error parsing SQL endpoint file: {File}", file);
                }
            }

            Log.Debug($"✅ Loaded {endpoints.Count} SQL endpoints from {endpointsDirectory}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ Error scanning SQL endpoints directory: {Directory}", endpointsDirectory);
        }

        return endpoints;
    }

    /// <summary>
    /// Internal method to load all SQL webhook endpoints from the endpoints directory
    /// </summary>
    private static Dictionary<string, EndpointDefinition> LoadSqlWebhookEndpoints(string endpointsDirectory)
    {
        var endpoints = new Dictionary<string, EndpointDefinition>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (!Directory.Exists(endpointsDirectory))
            {
                Log.Warning($"⚠️ SQL Webhook endpoints directory not found: {endpointsDirectory}");
                Directory.CreateDirectory(endpointsDirectory);
                return endpoints;
            }

            // Get all JSON files in the endpoints directory and subdirectories
            foreach (var file in Directory.GetFiles(endpointsDirectory, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    // Read and parse the endpoint definition
                    var json = File.ReadAllText(file);
                    var definition = ParseSqlEndpointDefinition(json);

                    if (definition != null && !string.IsNullOrWhiteSpace(definition.DatabaseObjectName))
                    {
                        // Extract endpoint name from directory name
                        var endpointName = Path.GetFileName(Path.GetDirectoryName(file)) ?? "";

                        // Skip if no valid name could be extracted
                        if (string.IsNullOrWhiteSpace(endpointName))
                        {
                            Log.Warning("⚠️ Could not determine endpoint name for {File}", file);
                            continue;
                        }

                        // Add the endpoint to the dictionary
                        endpoints[endpointName] = definition;

                        Log.Debug($"📊 SQL Webhook Endpoint: {endpointName}; Object: {definition.DatabaseSchema}.{definition.DatabaseObjectName}");
                    }
                    else
                    {
                        Log.Warning("⚠️ Failed to load SQL webhook endpoint from {File}", file);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "❌ Error parsing SQL webhook endpoint file: {File}", file);
                }
            }

            Log.Debug($"✅ Loaded {endpoints.Count} webhook endpoints from {endpointsDirectory}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ Error scanning SQL webhook endpoints directory: {Directory}", endpointsDirectory);
        }

        return endpoints;
    }

    /// <summary>
    /// Internal method to load all file endpoints from the endpoints directory
    /// </summary>
    private static Dictionary<string, EndpointDefinition> LoadFileEndpoints(string endpointsDirectory)
    {
        var endpoints = new Dictionary<string, EndpointDefinition>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (!Directory.Exists(endpointsDirectory))
            {
                Log.Warning($"⚠️ File endpoints directory not found: {endpointsDirectory}");
                Directory.CreateDirectory(endpointsDirectory);
                return endpoints;
            }

            // Get all JSON files in the endpoints directory and subdirectories
            foreach (var file in Directory.GetFiles(endpointsDirectory, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    // Read and parse the endpoint definition
                    var json = File.ReadAllText(file);
                    var definition = ParseFileEndpointDefinition(json);

                    if (definition != null)
                    {
                        // Extract endpoint name from directory name
                        var endpointName = Path.GetFileName(Path.GetDirectoryName(file)) ?? "";

                        // Skip if no valid name could be extracted
                        if (string.IsNullOrWhiteSpace(endpointName))
                        {
                            Log.Warning("⚠️ Could not determine endpoint name for {File}", file);
                            continue;
                        }

                        // Add the endpoint to the dictionary
                        endpoints[endpointName] = definition;

                        Log.Debug("📁 File Endpoint: {Name} ({IsPrivate})",
                            endpointName,
                            definition.IsPrivate ? "Private" : "Public");
                    }
                    else
                    {
                        Log.Warning("⚠️ Failed to load file endpoint from {File}", file);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "❌ Error parsing file endpoint file: {File}", file);
                }
            }

            Log.Debug($"✅ Loaded {endpoints.Count} file endpoints from {endpointsDirectory}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ Error scanning file endpoints directory: {Directory}", endpointsDirectory);
        }

        return endpoints;
    }

    /// <summary>
    /// Internal method to load all static endpoints from the endpoints directory
    /// </summary>
    private static Dictionary<string, EndpointDefinition> LoadStaticEndpoints(string endpointsDirectory)
    {
        var endpoints = new Dictionary<string, EndpointDefinition>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (!Directory.Exists(endpointsDirectory))
            {
                Log.Warning($"⚠️ Static endpoints directory not found: {endpointsDirectory}");
                Directory.CreateDirectory(endpointsDirectory);
                return endpoints;
            }

            // Get all entity.json files in the endpoints directory subdirectories
            foreach (var file in Directory.GetFiles(endpointsDirectory, "entity.json", SearchOption.AllDirectories))
            {
                try
                {
                    // Read and parse the endpoint definition
                    var json = File.ReadAllText(file);
                    var definition = ParseStaticEndpointDefinition(json);

                    if (definition != null)
                    {
                        // Extract endpoint name from directory name
                        var endpointName = Path.GetFileName(Path.GetDirectoryName(file)) ?? "";

                        // Skip if no valid name could be extracted
                        if (string.IsNullOrWhiteSpace(endpointName))
                        {
                            Log.Warning("⚠️ Could not determine endpoint name for {File}", file);
                            continue;
                        }

                        // Add the endpoint to the dictionary
                        endpoints[endpointName] = definition;

                        Log.Debug("📄 Static Endpoint: {Name} ({IsPrivate}) - {ContentType}",
                            endpointName,
                            definition.IsPrivate ? "Private" : "Public",
                            definition.Properties?.GetValueOrDefault("ContentType", "unknown"));
                    }
                    else
                    {
                        Log.Warning("⚠️ Failed to load static endpoint from {File}", file);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "❌ Error parsing static endpoint file: {File}", file);
                }
            }

            Log.Debug($"✅ Loaded {endpoints.Count} static endpoints from {endpointsDirectory}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ Error scanning static endpoints directory: {Directory}", endpointsDirectory);
        }

        return endpoints;
    }

    /// <summary>
    /// Parses a file endpoint definition from JSON
    /// </summary>
    private static EndpointDefinition? ParseFileEndpointDefinition(string json)
    {
        try
        {
            var entity = JsonSerializer.Deserialize<FileEndpointEntity>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (entity != null)
            {
                return new EndpointDefinition
                {
                    Type = EndpointType.Files,
                    AllowedEnvironments = entity.AllowedEnvironments,
                    IsPrivate = entity.IsPrivate,
                    // Store file-specific properties in Properties dictionary
                    Properties = new Dictionary<string, object>
                    {
                        ["StorageType"] = entity.StorageType,
                        ["BaseDirectory"] = entity.BaseDirectory ?? "",
                        ["AllowedExtensions"] = entity.AllowedExtensions ?? new List<string>()
                    }
                };
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error parsing file endpoint definition");
        }

        return null;
    }

    /// <summary>
    /// Parses a static endpoint definition from JSON
    /// </summary>
    private static EndpointDefinition? ParseStaticEndpointDefinition(string json)
    {
        try
        {
            var entity = JsonSerializer.Deserialize<StaticEndpointEntity>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (entity != null)
            {
                return new EndpointDefinition
                {
                    Type = EndpointType.Static,
                    AllowedEnvironments = entity.AllowedEnvironments,
                    IsPrivate = entity.IsPrivate,
                    Documentation = entity.Documentation,
                    // Store static-specific properties in Properties dictionary
                    Properties = new Dictionary<string, object>
                    {
                        ["ContentType"] = entity.ContentType,
                        ["ContentFile"] = entity.ContentFile,
                        ["EnableFiltering"] = entity.EnableFiltering
                    }
                };
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error parsing static endpoint definition");
        }

        return null;
    }

    public static Dictionary<string, EndpointDefinition> GetFileEndpoints()
    {
        string fileEndpointsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "endpoints", "Files");
        LoadFileEndpointsIfNeeded(fileEndpointsDirectory);
        return _loadedFileEndpoints!;
    }

    /// <summary>
    /// Gets Static endpoints from the /endpoints/Static directory
    /// </summary>
    public static Dictionary<string, EndpointDefinition> GetStaticEndpoints()
    {
        string staticEndpointsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "endpoints", "Static");
        LoadStaticEndpointsIfNeeded(staticEndpointsDirectory);
        return _loadedStaticEndpoints!;
    }

    /// <summary>
    /// Parses a proxy endpoint definition from JSON, handling both legacy and extended formats
    /// </summary>
    private static EndpointDefinition? ParseProxyEndpointDefinition(string json)
    {
        try
        {
            // First try to parse as an ExtendedEndpointEntity (preferred format)
            var extendedEntity = JsonSerializer.Deserialize<ExtendedEndpointEntity>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (extendedEntity != null && !string.IsNullOrWhiteSpace(extendedEntity.Url) && extendedEntity.Methods != null)
            {
                return new EndpointDefinition
                {
                    Url = extendedEntity.Url,
                    Methods = extendedEntity.Methods,
                    IsPrivate = extendedEntity.IsPrivate,
                    Type = ParseEndpointType(extendedEntity.Type),
                    CompositeConfig = extendedEntity.CompositeConfig,
                    AllowedEnvironments = extendedEntity.AllowedEnvironments,
                    Documentation = extendedEntity.Documentation
                };
            }

            // Try to parse as a standard EndpointEntity as fallback
            var entity = JsonSerializer.Deserialize<EndpointEntity>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (entity != null && !string.IsNullOrWhiteSpace(entity.Url) && entity.Methods != null)
            {
                return new EndpointDefinition
                {
                    Url = entity.Url,
                    Methods = entity.Methods,
                    IsPrivate = false, // Legacy format doesn't support IsPrivate
                    Type = EndpointType.Standard,
                    CompositeConfig = null,
                    AllowedEnvironments = entity.AllowedEnvironments,
                    Documentation = entity.Documentation
                };
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error parsing proxy endpoint definition");
        }

        return null;
    }

    /// <summary>
    /// Parses a SQL endpoint definition from JSON
    /// </summary>
    private static EndpointDefinition? ParseSqlEndpointDefinition(string json)
    {
        try
        {
            // Parse as EndpointEntity
            var entity = JsonSerializer.Deserialize<EndpointEntity>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (entity != null && !string.IsNullOrWhiteSpace(entity.DatabaseObjectName))
            {
                var allowedMethods = entity.AllowedMethods ?? new List<string> { "GET" };
                var schema = entity.DatabaseSchema ?? "dbo";

                // Validate the endpoint configuration before creating the definition
                var validationResults = ValidateSqlEndpointConfiguration(entity);
                if (validationResults.Any())
                {
                    var errors = string.Join(", ", validationResults);
                    Log.Error("SQL endpoint validation failed: {Errors}", errors);
                    throw new InvalidOperationException($"SQL endpoint configuration is invalid: {errors}");
                }

                return new EndpointDefinition
                {
                    Type = EndpointType.SQL,
                    DatabaseObjectName = entity.DatabaseObjectName,
                    DatabaseSchema = schema,
                    AllowedColumns = entity.AllowedColumns ?? new List<string>(),
                    Procedure = entity.Procedure,
                    PrimaryKey = entity.PrimaryKey,
                    Methods = allowedMethods,
                    AllowedEnvironments = entity.AllowedEnvironments,
                    Documentation = entity.Documentation
                };
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error parsing SQL endpoint definition");
        }

        return null;
    }

    /// <summary>
    /// Validates SQL endpoint configuration to prevent runtime errors
    /// </summary>
    private static List<string> ValidateSqlEndpointConfiguration(EndpointEntity entity)
    {
        var errors = new List<string>();

        // Validate AllowedColumns for malformed entries
        if (entity.AllowedColumns != null)
        {
            for (int i = 0; i < entity.AllowedColumns.Count; i++)
            {
                var column = entity.AllowedColumns[i];
                
                if (string.IsNullOrWhiteSpace(column))
                {
                    errors.Add($"AllowedColumns[{i}] is empty or whitespace");
                    continue;
                }

                // Check for problematic patterns that could cause IndexOutOfRangeException
                var parts = column.Split(';', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                {
                    errors.Add($"AllowedColumns[{i}] '{column}' contains only separators and is invalid");
                }
                else if (parts.Any(part => string.IsNullOrWhiteSpace(part)))
                {
                    errors.Add($"AllowedColumns[{i}] '{column}' contains empty parts");
                }
            }
        }

        // Validate required fields
        if (string.IsNullOrWhiteSpace(entity.DatabaseObjectName))
        {
            errors.Add("DatabaseObjectName is required for SQL endpoints");
        }

        // Validate allowed methods
        if (entity.AllowedMethods != null)
        {
            var validMethods = new[] { "GET", "POST", "PUT", "DELETE", "MERGE" };
            var invalidMethods = entity.AllowedMethods.Where(m => !validMethods.Contains(m.ToUpper())).ToList();
            if (invalidMethods.Any())
            {
                errors.Add($"Invalid HTTP methods: {string.Join(", ", invalidMethods)}");
            }
        }

        return errors;
    }

    /// <summary>
    /// Converts a string type to the EndpointType enum
    /// </summary>
    private static EndpointType ParseEndpointType(string? typeString)
    {
        if (string.IsNullOrWhiteSpace(typeString))
            return EndpointType.Standard;

        return typeString.ToLowerInvariant() switch
        {
            "composite" => EndpointType.Composite,
            "sql" => EndpointType.SQL,
            "private" => EndpointType.Private,
            _ => EndpointType.Standard
        };
    }

    /// <summary>
    /// Logs information about a loaded endpoint with appropriate emoji based on type
    /// </summary>
    private static void LogEndpointLoading(string endpointName, EndpointDefinition definition)
    {
        if (definition.IsPrivate)
        {
            Log.Debug("🔒 Loaded private endpoint: {Name} -> {Url}", endpointName, definition.Url);
        }
        else if (definition.IsComposite)
        {
            Log.Debug("🧩 Loaded composite endpoint: {Name} -> {Url}", endpointName, definition.Url);
        }
        else if (definition.IsSql)
        {
            Log.Debug("📊 Loaded SQL endpoint: {Name} -> {ObjectName}", endpointName, definition.DatabaseObjectName);
        }
        else
        {
            Log.Debug("♨️ Loaded standard endpoint: {Name} -> {Url}", endpointName, definition.Url);
        }
    }

    /// <summary>
    /// Creates sample endpoint definitions if none exist
    /// </summary>
    public static void CreateSampleEndpoints(string baseDirectory)
    {
        try
        {
            // Create SQL endpoint directory
            var sqlEndpointsDir = Path.Combine(baseDirectory, "SQL");
            if (!Directory.Exists(sqlEndpointsDir))
            {
                Directory.CreateDirectory(sqlEndpointsDir);
            }

            // Create SQL sample endpoint
            CreateSampleSqlEndpoint(sqlEndpointsDir);

            // Create Proxy endpoint directory
            var proxyEndpointsDir = Path.Combine(baseDirectory, "Proxy");
            if (!Directory.Exists(proxyEndpointsDir))
            {
                Directory.CreateDirectory(proxyEndpointsDir);
            }

            // Create Proxy sample endpoint
            CreateSampleProxyEndpoint(proxyEndpointsDir);

            // Create Composite sample endpoint
            CreateSampleCompositeEndpoint(proxyEndpointsDir);

            // Create Webhook directory
            var webhookDir = Path.Combine(baseDirectory, "Webhooks");
            if (!Directory.Exists(webhookDir))
            {
                Directory.CreateDirectory(webhookDir);
            }

            // Create Webhook sample endpoint
            CreateSampleWebhookEndpoint(webhookDir);

            // Create Files endpoint directory
            var filesEndpointsDir = Path.Combine(baseDirectory, "Files");
            if (!Directory.Exists(filesEndpointsDir))
            {
                Directory.CreateDirectory(filesEndpointsDir);
            }
            // Create Files sample endpoint
            CreateSampleFileEndpoint(filesEndpointsDir);

            // Clear the cached endpoints to force a reload
            lock (_loadLock)
            {
                _loadedProxyEndpoints = null;
                _loadedSqlEndpoints = null;
            }

            Log.Information("✅ Created sample endpoints in each endpoint directory");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ Error creating sample endpoint definitions");
        }
    }

    private static void CreateSampleSqlEndpoint(string sqlEndpointsDir)
    {
        var sampleDir = Path.Combine(sqlEndpointsDir, "Items");
        if (!Directory.Exists(sampleDir))
        {
            Directory.CreateDirectory(sampleDir);
        }

        var samplePath = Path.Combine(sampleDir, "entity.json");
        if (!File.Exists(samplePath))
        {
            var sample = new EndpointEntity
            {
                DatabaseObjectName = "Items",
                DatabaseSchema = "dbo",
                AllowedColumns = new List<string> { "ItemCode", "Description", "Price" },
                AllowedMethods = new List<string> { "GET" },
                PrimaryKey = "ItemCode"
            };

            var json = JsonSerializer.Serialize(sample, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(samplePath, json);
            Log.Information($"✅ Created sample SQL endpoint definition: {samplePath}");
        }
    }

    private static void CreateSampleProxyEndpoint(string proxyEndpointsDir)
    {
        var sampleDir = Path.Combine(proxyEndpointsDir, "Sample");
        if (!Directory.Exists(sampleDir))
        {
            Directory.CreateDirectory(sampleDir);
        }

        var samplePath = Path.Combine(sampleDir, "entity.json");
        if (!File.Exists(samplePath))
        {
            var sample = new ExtendedEndpointEntity
            {
                Url = "https://jsonplaceholder.typicode.com/posts",
                Methods = new List<string> { "GET", "POST" },
                Type = "Standard",
                IsPrivate = false
            };

            var json = JsonSerializer.Serialize(sample, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(samplePath, json);
            Log.Information($"✅ Created sample proxy endpoint definition: {samplePath}");
        }
    }

    private static void CreateSampleCompositeEndpoint(string proxyEndpointsDir)
    {
        var compositeSampleDir = Path.Combine(proxyEndpointsDir, "SampleComposite");
        if (!Directory.Exists(compositeSampleDir))
        {
            Directory.CreateDirectory(compositeSampleDir);
        }

        var compositeSamplePath = Path.Combine(compositeSampleDir, "entity.json");
        if (!File.Exists(compositeSamplePath))
        {
            var compositeSample = new ExtendedEndpointEntity
            {
                Url = "http://localhost:8020/services/Exact.Entity.REST.EG",
                Methods = new List<string> { "POST" },
                Type = "Composite",
                CompositeConfig = new CompositeDefinition
                {
                    Name = "SampleComposite",
                    Description = "Sample composite endpoint",
                    Steps = new List<CompositeStep>
                    {
                        new CompositeStep
                        {
                            Name = "Step1",
                            Endpoint = "SampleEndpoint1",
                            Method = "POST",
                            TemplateTransformations = new Dictionary<string, string>
                            {
                                { "TransactionKey", "$guid" }
                            }
                        },
                        new CompositeStep
                        {
                            Name = "Step2",
                            Endpoint = "SampleEndpoint2",
                            Method = "POST",
                            DependsOn = "Step1",
                            TemplateTransformations = new Dictionary<string, string>
                            {
                                { "TransactionKey", "$prev.Step1.TransactionKey" }
                            }
                        }
                    }
                }
            };

            var json = JsonSerializer.Serialize(compositeSample, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(compositeSamplePath, json);
            Log.Information($"✅ Created sample composite endpoint definition: {compositeSamplePath}");
        }
    }

    private static void CreateSampleWebhookEndpoint(string webhookDir)
    {
        var samplePath = Path.Combine(webhookDir, "entity.json");
        if (!File.Exists(samplePath))
        {
            var sample = new EndpointEntity
            {
                DatabaseObjectName = "WebhookData",
                DatabaseSchema = "dbo",
                AllowedColumns = new List<string> { "webhook1", "webhook2" }
            };

            var json = JsonSerializer.Serialize(sample, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(samplePath, json);
            Log.Information($"✅ Created sample webhook endpoint definition: {samplePath}");
        }
    }
    
    private static void CreateSampleFileEndpoint(string filesEndpointsDir)
    {
        var sampleDir = Path.Combine(filesEndpointsDir, "SampleFiles");
        if (!Directory.Exists(sampleDir))
        {
            Directory.CreateDirectory(sampleDir);
        }

        var samplePath = Path.Combine(sampleDir, "entity.json");
        if (!File.Exists(samplePath))
        {
            var sample = new FileEndpointEntity
            {
                StorageType = "Local",
                BaseDirectory = "sample",
                AllowedExtensions = new List<string> { ".jpg", ".png", ".pdf", ".docx", ".xlsx" },
                IsPrivate = false,
                AllowedEnvironments = new List<string> { "prod", "dev" }
            };

            var json = JsonSerializer.Serialize(sample, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(samplePath, json);
            Log.Information($"✅ Created sample Files endpoint definition: {samplePath}");
        }
    }
}