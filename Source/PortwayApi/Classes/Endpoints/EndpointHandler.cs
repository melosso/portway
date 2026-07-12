namespace PortwayApi.Classes;

using System.Text.Json;
using Serilog;
using PortwayApi.Helpers;

/// <summary>Unified endpoint definition that handles all endpoint types</summary>
public class EndpointDefinition
{
    public string Url { get; set; } = string.Empty;
    public List<string> Methods { get; set; } = new List<string>();
    public EndpointType Type { get; set; } = EndpointType.Standard;
    public CompositeDefinition? CompositeConfig { get; set; }
    public bool IsPrivate { get; set; } = false;
    /// <summary>Marks the endpoint's operations as deprecated in the OpenAPI document</summary>
    public bool Deprecated { get; set; } = false;
    public McpSettings? Mcp { get; set; }
    /// <summary>Computed from Mcp.Exposed for backward compatibility with tuple consumers.</summary>
    public bool IsMcpExposed => Mcp?.Exposed == true;

    // SQL endpoint properties
    public string? DatabaseObjectName { get; set; }
    public string? DatabaseSchema { get; set; }
    public List<string>? AllowedColumns { get; set; }
    public List<string>? RequiredColumns { get; set; }
    public Dictionary<string, ColumnValidationRule>? ColumnValidation { get; set; }
    public string? Procedure { get; set; }
    public string? PrimaryKey { get; set; }
    
    public string? DatabaseObjectType { get; set; } = "Table"; // Table, View, TableValuedFunction
    public List<TVFParameter>? FunctionParameters { get; set; }
    
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
    
    // Custom properties for extended functionality
    public Dictionary<string, object>? CustomProperties { get; set; }

    // DELETE operation patterns
    public List<DeletePattern>? DeletePatterns { get; set; }

    /// <summary>Optional namespace for grouping related endpoints (e.g., "CRM", "Inventory") Takes precedence over folder-inferred namespace</summary>
    public string? Namespace { get; set; }
    
    /// <summary>Display name for this specific endpoint (e.g., "Account Management") Used in OpenAPI documentation and UI displays</summary>
    public string? DisplayName { get; set; }
    
    /// <summary>Display name for the namespace (e.g., "Customer Relationship Management") Used as documentation tag description and documentation grouping</summary>
    public string? NamespaceDisplayName { get; set; }

    /// <summary>Optional Scalar sidebar icon for this endpoint's namespace tag (Phosphor set, e.g. "phosphor/regular/package")</summary>
    public string? NamespaceIcon { get; set; }

    /// <summary>Folder name where the endpoint definition is located (for backward compatibility) Used as fallback for DocumentationTag when DisplayName is not specified</summary>
    public string? FolderName { get; set; }
    
    /// <summary>Namespace inferred from folder structure (for internal use)</summary>
    public string? InferredNamespace { get; set; }

    // Helper properties to simplify type checking
    public bool IsStandard => Type == EndpointType.Standard && !IsPrivate;
    public bool IsComposite => Type == EndpointType.Composite || 
                              (CompositeConfig != null && !string.IsNullOrEmpty(CompositeConfig.Name));
    public bool IsSql => Type == EndpointType.SQL;
    public bool IsStatic => Type == EndpointType.Static;
    
    // Namespace helper properties
    /// <summary>Gets the effective namespace (explicit namespace takes precedence over inferred)</summary>
    public string? EffectiveNamespace => Namespace ?? InferredNamespace;
    
    /// <summary>Indicates if this endpoint has a namespace (explicit or inferred)</summary>
    public bool HasNamespace => !string.IsNullOrEmpty(EffectiveNamespace);
    
    /// <summary>Gets the endpoint name for URL and key generation</summary>
    public string EndpointName => FolderName ?? (IsSql ? DatabaseObjectName : Path.GetFileNameWithoutExtension(Url)) ?? "Unknown";
    
    /// <summary>Gets the full path including namespace (for routing keys)</summary>
    public string FullPath => HasNamespace ? $"{EffectiveNamespace}/{EndpointName}" : EndpointName;
    
    /// <summary>Gets the display path for documentation and UI</summary>
    public string DisplayPath => HasNamespace && !string.IsNullOrEmpty(DisplayName) 
        ? $"{NamespaceDisplayName ?? EffectiveNamespace} - {DisplayName}" 
        : DisplayName ?? EndpointName;
    
    /// <summary>Gets the appropriate documentation tag name for OpenAPI grouping</summary>
    public string DocumentationTag
    {
        get
        {
            var result = !string.IsNullOrEmpty(Namespace) ? (NamespaceDisplayName ?? Namespace)
                       : HasNamespace ? (NamespaceDisplayName ?? EffectiveNamespace!)
                       : (DisplayName ?? FolderName ?? EndpointName);

            System.Diagnostics.Debug.WriteLine($"DocumentationTag Debug - Namespace: '{Namespace}', InferredNamespace: '{InferredNamespace}', EffectiveNamespace: '{EffectiveNamespace}', Result: '{result}'");

            return result;
        }
    }

    /// <summary>Creates URL patterns for routing (supports both namespaced and non-namespaced)</summary>
    public List<string> GetUrlPatterns()
    {
        var patterns = new List<string>();
        
        if (HasNamespace)
        {
            // Primary pattern with namespace
            patterns.Add($"/api/{{env}}/{EffectiveNamespace}/{EndpointName}");
            patterns.Add($"/api/{{env}}/{EffectiveNamespace}/{EndpointName}/{{id}}");
        }
        
        // Fallback pattern without namespace (for backward compatibility)
        patterns.Add($"/api/{{env}}/{EndpointName}");
        patterns.Add($"/api/{{env}}/{EndpointName}/{{id}}");
        
        return patterns;
    }

    /// <summary>Validates namespace naming conventions</summary>
    public List<string> ValidateNamespace()
    {
        var errors = new List<string>();
        var namespaceToCheck = EffectiveNamespace;

        if (!string.IsNullOrEmpty(namespaceToCheck))
        {
            // Check namespace naming rules
            if (!System.Text.RegularExpressions.Regex.IsMatch(namespaceToCheck, @"^[A-Za-z][A-Za-z0-9_]*$"))
            {
                errors.Add("Namespace must start with a letter and contain only letters, numbers, and underscores");
            }

            if (namespaceToCheck.Length > 50)
            {
                errors.Add("Namespace cannot exceed 50 characters");
            }

            // Reserved namespace names
            var reserved = new[] { "api", "docs", "openapi", "health", "admin", "system", "composite", "webhook", "files" };
            if (reserved.Contains(namespaceToCheck.ToLowerInvariant()))
            {
                errors.Add($"'{namespaceToCheck}' is a reserved namespace name");
            }
        }

        return errors;
    }
    
    /// <summary>Converts EndpointDefinition to the ProxyEndpointInfo snapshot used by composite and MCP consumers</summary>
    public ProxyEndpointInfo ToProxyEndpointInfo()
    {
        return new ProxyEndpointInfo(
            Url,
            new HashSet<string>(Methods, StringComparer.OrdinalIgnoreCase),
            IsPrivate,
            IsMcpExposed,
            Type.ToString(),
            AllowedEnvironments
        );
    }
}

public static class EndpointHandler
{
    // Cache for loaded endpoints to avoid multiple loads
    private static volatile Dictionary<string, EndpointDefinition>? _loadedProxyEndpoints = null;
    private static volatile Dictionary<string, EndpointDefinition>? _loadedSqlEndpoints = null;
    private static volatile Dictionary<string, EndpointDefinition>? _loadedSqlWebhookEndpoints = null;
    private static volatile Dictionary<string, EndpointDefinition>? _loadedFileEndpoints = null;
    private static volatile Dictionary<string, EndpointDefinition>? _loadedStaticEndpoints = null;
    private static readonly object _loadLock = new object();

    private static readonly EndpointLoaderSpec ProxyLoaderSpec = new(
        "Proxy", "proxy", "", "*.json", NamespaceAware: true,
        ParseProxyEndpointDefinition,
        d => !string.IsNullOrWhiteSpace(d.Url) && d.Methods.Any(),
        LogEndpointLoading);

    private static readonly EndpointLoaderSpec SqlLoaderSpec = new(
        "SQL", "SQL", "SQL ", "*.json", NamespaceAware: true,
        ParseSqlEndpointDefinition,
        d => !string.IsNullOrWhiteSpace(d.DatabaseObjectName),
        (key, d) => Log.Debug($"SQL Endpoint: {key}; Object: {d.DatabaseSchema}.{d.DatabaseObjectName}; Namespace: {d.EffectiveNamespace ?? "None"}"));

    private static readonly EndpointLoaderSpec StaticLoaderSpec = new(
        "Static", "static", "static ", "entity.json", NamespaceAware: true,
        ParseStaticEndpointDefinition,
        _ => true,
        (key, d) => Log.Debug("Static Endpoint: {Name} ({IsPrivate}) - {ContentType} | DocumentationTag: {DocumentationTag} | Namespace: {Namespace} | InferredNamespace: {InferredNamespace}",
            key,
            d.IsPrivate ? "Private" : "Public",
            d.Properties?.GetValueOrDefault("ContentType", "unknown"),
            d.DocumentationTag,
            d.Namespace ?? "null",
            d.InferredNamespace ?? "null"));

    private static readonly EndpointLoaderSpec FileLoaderSpec = new(
        "File", "file", "file ", "*.json", NamespaceAware: true,
        ParseFileEndpointDefinition,
        _ => true,
        (key, d) => Log.Debug("File Endpoint: {Name} ({IsPrivate}) | Namespace: {Namespace}",
            key, d.IsPrivate ? "Private" : "Public", d.EffectiveNamespace ?? "None"));

    private static readonly EndpointLoaderSpec WebhookLoaderSpec = new(
        "Webhook", "webhook", "webhook ", "entity.json", NamespaceAware: true,
        ParseWebhookEndpointDefinition,
        d => !string.IsNullOrWhiteSpace(d.DatabaseObjectName),
        (key, d) => Log.Debug("Webhook Endpoint: {Name}; Object: {Schema}.{Object}; Namespace: {Namespace}",
            key, d.DatabaseSchema, d.DatabaseObjectName, d.EffectiveNamespace ?? "None"));

    /// <summary>Resolves the endpoints folder path, supporting both "Endpoints" and "endpoints" for cross-platform compatibility</summary>
    private static string GetEndpointsBasePath()
    {
        var baseDir = Directory.GetCurrentDirectory();
        return Path.Combine(baseDir, "endpoints");
    }

    /// <summary>Gets SQL endpoints from the /endpoints/SQL directory</summary>
    public static Dictionary<string, EndpointDefinition> GetSqlEndpoints()
    {
        string sqlEndpointsDirectory = Path.Combine(GetEndpointsBasePath(), "SQL");
        LoadSqlEndpointsIfNeeded(sqlEndpointsDirectory);
        return _loadedSqlEndpoints!;
    }

    /// <summary>Gets SQL webhook endpoints from the /endpoints/Webhooks directory</summary>
    public static Dictionary<string, EndpointDefinition> GetSqlWebhookEndpoints()
    {
        string sqlWebhookEndpointsDirectory = Path.Combine(GetEndpointsBasePath(), "Webhooks");
        LoadSqlWebhookEndpointsIfNeeded(sqlWebhookEndpointsDirectory);
        return _loadedSqlWebhookEndpoints!;
    }

    /// <summary>Gets Proxy endpoints from the /endpoints/Proxy directory</summary>
    public static Dictionary<string, EndpointDefinition> GetProxyEndpoints()
    {
        string proxyEndpointsDirectory = Path.Combine(GetEndpointsBasePath(), "Proxy");
        LoadProxyEndpointsIfNeeded(proxyEndpointsDirectory);
        return _loadedProxyEndpoints!;
    }

    /// <summary>Gets all composite endpoint definitions from the endpoints directory</summary>
    public static Dictionary<string, CompositeDefinition> GetCompositeDefinitions(Dictionary<string, ProxyEndpointInfo> endpointMap)
    {
        // We already have endpoints loaded, so just extract the composite configs
        var compositeDefinitions = new Dictionary<string, CompositeDefinition>(StringComparer.OrdinalIgnoreCase);

        // If proxy endpoints haven't been loaded yet, load them
        if (_loadedProxyEndpoints == null)
        {
            string proxyEndpointsDirectory = Path.Combine(GetEndpointsBasePath(), "Proxy");
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

    /// <summary>Loads file endpoints if they haven't been loaded yet</summary>
    private static void LoadFileEndpointsIfNeeded(string endpointsDirectory)
    {
        if (_loadedFileEndpoints == null)
            lock (_loadLock)
                _loadedFileEndpoints ??= LoadFileEndpoints(endpointsDirectory);
    }

    /// <summary>Loads static endpoints if they haven't been loaded yet</summary>
    private static void LoadStaticEndpointsIfNeeded(string endpointsDirectory)
    {
        if (_loadedStaticEndpoints == null)
            lock (_loadLock)
                _loadedStaticEndpoints ??= LoadStaticEndpoints(endpointsDirectory);
    }

    /// <summary>Scans the specified directory for endpoint definition files and returns a dictionary of endpoints</summary>
    /// <param name="endpointsDirectory">Directory containing endpoint definitions</param>
    /// <returns>Dictionary with endpoint names as keys and tuples of (url, methods, isPrivate, isMcpExposed, type) as values</returns>
    public static Dictionary<string, ProxyEndpointInfo> GetEndpoints(string endpointsDirectory)
    {
        // Check if the directory is for proxy or SQL endpoints
        bool isProxyEndpoint = endpointsDirectory.Contains("Proxy", StringComparison.OrdinalIgnoreCase);

        // Load endpoints if not already loaded
        if (isProxyEndpoint)
        {
            LoadProxyEndpointsIfNeeded(endpointsDirectory);

            // Convert to the legacy format (includes AllowedEnvironments for composite step validation)
            var endpointMap = new Dictionary<string, ProxyEndpointInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in _loadedProxyEndpoints!)
            {
                endpointMap[kvp.Key] = kvp.Value.ToProxyEndpointInfo();
            }

            return endpointMap;
        }
        else
        {
            // Create an empty dictionary for now - SQL endpoints are handled differently
            return new Dictionary<string, ProxyEndpointInfo>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>Internal method to load proxy endpoints if they haven't been loaded yet</summary>
    private static void LoadProxyEndpointsIfNeeded(string endpointsDirectory)
    {
        if (_loadedProxyEndpoints == null)
            lock (_loadLock)
                _loadedProxyEndpoints ??= LoadProxyEndpoints(endpointsDirectory);
    }

    /// <summary>Internal method to load SQL endpoints if they haven't been loaded yet</summary>
    private static void LoadSqlEndpointsIfNeeded(string endpointsDirectory)
    {
        if (_loadedSqlEndpoints == null)
            lock (_loadLock)
                _loadedSqlEndpoints ??= LoadSqlEndpoints(endpointsDirectory);
    }

    /// <summary>Internal method to load SQL endpoints if they haven't been loaded yet</summary>
    private static void LoadSqlWebhookEndpointsIfNeeded(string endpointsDirectory)
    {
        if (_loadedSqlWebhookEndpoints == null)
            lock (_loadLock)
                _loadedSqlWebhookEndpoints ??= LoadSqlWebhookEndpoints(endpointsDirectory);
    }

    /// <summary>Internal method to load all proxy endpoints from the endpoints directory</summary>
    private static Dictionary<string, EndpointDefinition> LoadProxyEndpoints(string endpointsDirectory)
        => EndpointDirectoryLoader.Load(endpointsDirectory, ProxyLoaderSpec);

    /// <summary>Internal method to load all SQL endpoints from the endpoints directory</summary>
    private static Dictionary<string, EndpointDefinition> LoadSqlEndpoints(string endpointsDirectory)
        => EndpointDirectoryLoader.Load(endpointsDirectory, SqlLoaderSpec);

    /// <summary>Internal method to load all webhook endpoints from the endpoints directory (namespace-aware)</summary>
    private static Dictionary<string, EndpointDefinition> LoadSqlWebhookEndpoints(string endpointsDirectory)
        => EndpointDirectoryLoader.Load(endpointsDirectory, WebhookLoaderSpec);

    /// <summary>Parses a webhook endpoint definition; reuses the SQL parser and tags the result as a Webhook</summary>
    private static EndpointDefinition? ParseWebhookEndpointDefinition(string json)
    {
        var definition = ParseSqlEndpointDefinition(json);
        if (definition != null)
        {
            definition.Type = EndpointType.Webhook;
        }
        return definition;
    }

    /// <summary>Internal method to load all file endpoints from the endpoints directory</summary>
    private static Dictionary<string, EndpointDefinition> LoadFileEndpoints(string endpointsDirectory)
        => EndpointDirectoryLoader.Load(endpointsDirectory, FileLoaderSpec);

    /// <summary>Internal method to load all static endpoints from the endpoints directory</summary>
    private static Dictionary<string, EndpointDefinition> LoadStaticEndpoints(string endpointsDirectory)
        => EndpointDirectoryLoader.Load(endpointsDirectory, StaticLoaderSpec);

    /// <summary>Parses a file endpoint definition from JSON</summary>
    private static EndpointDefinition? ParseFileEndpointDefinition(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        var entity = JsonSerializer.Deserialize<FileEndpointEntity>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (entity == null)
            return null;

        return new EndpointDefinition
        {
            Type = EndpointType.Files,
            Methods = new List<string> { "GET", "POST", "DELETE" },
            AllowedEnvironments = entity.AllowedEnvironments,
            IsPrivate = entity.IsPrivate,
            Mcp = entity.Mcp,
            Documentation = entity.Documentation,
            Namespace = entity.Namespace,
            DisplayName = entity.DisplayName,
            NamespaceDisplayName = entity.NamespaceDisplayName,
            NamespaceIcon = entity.NamespaceIcon,
            // Store file-specific properties in Properties dictionary
            Properties = new Dictionary<string, object>
            {
                ["StorageType"] = entity.StorageType,
                ["BaseDirectory"] = entity.BaseDirectory ?? "",
                ["AllowedExtensions"] = entity.AllowedExtensions ?? new List<string>()
            }
        };
    }

    /// <summary>Parses a static endpoint definition from JSON</summary>
    private static EndpointDefinition? ParseStaticEndpointDefinition(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        var entity = JsonSerializer.Deserialize<StaticEndpointEntity>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (entity == null)
            return null;

        return new EndpointDefinition
        {
            Type = EndpointType.Static,
            Methods = new List<string> { "GET" },
            AllowedEnvironments = entity.AllowedEnvironments,
            IsPrivate = entity.IsPrivate,
            Mcp = entity.Mcp,
            Documentation = entity.Documentation,
            Namespace = entity.Namespace,
            DisplayName = entity.DisplayName,
            NamespaceDisplayName = entity.NamespaceDisplayName,
            NamespaceIcon = entity.NamespaceIcon,
            // Store static-specific properties in Properties dictionary
            Properties = new Dictionary<string, object>
            {
                ["ContentType"] = entity.ContentType,
                ["ContentFile"] = entity.ContentFile,
                ["EnableFiltering"] = entity.EnableFiltering
            }
        };
    }

    public static Dictionary<string, EndpointDefinition> GetFileEndpoints()
    {
        string fileEndpointsDirectory = Path.Combine(GetEndpointsBasePath(), "Files");
        LoadFileEndpointsIfNeeded(fileEndpointsDirectory);
        return _loadedFileEndpoints!;
    }

    /// <summary>Gets Static endpoints from the /endpoints/Static directory</summary>
    public static Dictionary<string, EndpointDefinition> GetStaticEndpoints()
    {
        string staticEndpointsDirectory = Path.Combine(GetEndpointsBasePath(), "Static");
        LoadStaticEndpointsIfNeeded(staticEndpointsDirectory);
        return _loadedStaticEndpoints!;
    }

    /// <summary>Parses a proxy endpoint definition from JSON, handling both legacy and extended formats</summary>
    private static EndpointDefinition? ParseProxyEndpointDefinition(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

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
                Deprecated = extendedEntity.Deprecated,
                Mcp = extendedEntity.Mcp,
                Type = ParseEndpointType(extendedEntity.Type),
                CompositeConfig = extendedEntity.CompositeConfig,
                AllowedEnvironments = extendedEntity.AllowedEnvironments,
                Documentation = extendedEntity.Documentation,
                CustomProperties = extendedEntity.CustomProperties,
                Namespace = extendedEntity.Namespace,
                NamespaceDisplayName = extendedEntity.NamespaceDisplayName,
                NamespaceIcon = extendedEntity.NamespaceIcon,
                DisplayName = extendedEntity.DisplayName,
                DeletePatterns = extendedEntity.DeletePatterns
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
                IsPrivate = false,
                Mcp = entity.Mcp,
                Type = EndpointType.Standard,
                CompositeConfig = null,
                AllowedEnvironments = entity.AllowedEnvironments,
                Documentation = entity.Documentation,
                CustomProperties = entity.CustomProperties,
                Namespace = entity.Namespace,
                DisplayName = entity.DisplayName,
                NamespaceDisplayName = entity.NamespaceDisplayName,
            NamespaceIcon = entity.NamespaceIcon,
                DeletePatterns = entity.DeletePatterns
            };
        }

        return null;
    }

    /// <summary>Parses a SQL endpoint definition from JSON</summary>
    private static EndpointDefinition? ParseSqlEndpointDefinition(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        var entity = JsonSerializer.Deserialize<EndpointEntity>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (entity == null || string.IsNullOrWhiteSpace(entity.DatabaseObjectName))
            return null;

        var allowedMethods = entity.AllowedMethods ?? new List<string> { "GET" };
        var schema = entity.DatabaseSchema ?? "dbo";

        var validationResults = ValidateSqlEndpointConfiguration(entity);
        if (validationResults.Any())
        {
            var errors = string.Join(", ", validationResults);
            throw new InvalidOperationException($"Endpoint configuration is invalid: {errors}");
        }

        return new EndpointDefinition
        {
            Type = EndpointType.SQL,
            Deprecated = entity.Deprecated,
            DatabaseObjectName = entity.DatabaseObjectName,
            DatabaseSchema = schema,
            AllowedColumns = entity.AllowedColumns ?? new List<string>(),
            Procedure = entity.Procedure,
            PrimaryKey = entity.PrimaryKey,
            DatabaseObjectType = entity.DatabaseObjectType ?? "Table",
            FunctionParameters = entity.FunctionParameters,
            Methods = allowedMethods,
            Mcp = entity.Mcp,
            AllowedEnvironments = entity.AllowedEnvironments,
            Documentation = entity.Documentation,
            Namespace = entity.Namespace,
            NamespaceDisplayName = entity.NamespaceDisplayName,
            NamespaceIcon = entity.NamespaceIcon,
            DisplayName = entity.DisplayName
        };
    }

    /// <summary>Validates SQL endpoint configuration to prevent runtime errors</summary>
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

        // Validate Table Valued Function specific configuration
        if (!string.IsNullOrEmpty(entity.DatabaseObjectType) && 
            entity.DatabaseObjectType.Equals("TableValuedFunction", StringComparison.OrdinalIgnoreCase))
        {
            // Create a temporary endpoint definition for TVF validation
            var tempEndpoint = new EndpointDefinition
            {
                DatabaseObjectType = entity.DatabaseObjectType,
                DatabaseObjectName = entity.DatabaseObjectName,
                FunctionParameters = entity.FunctionParameters,
                Methods = entity.AllowedMethods ?? new List<string> { "GET" }
            };

            var tvfErrors = PortwayApi.Classes.Handlers.TableValuedFunctionSqlHandler.ValidateTVFConfiguration(tempEndpoint);
            errors.AddRange(tvfErrors);
        }

        // Validate allowed methods
        if (entity.AllowedMethods != null)
        {
            var validMethods = new[] { "GET", "POST", "PUT", "PATCH", "DELETE", "MERGE", "QUERY" };
            var invalidMethods = entity.AllowedMethods.Where(m => !validMethods.Contains(m.ToUpper())).ToList();
            if (invalidMethods.Any())
            {
                errors.Add($"Invalid HTTP methods: {string.Join(", ", invalidMethods)}");
            }
        }

        return errors;
    }

    /// <summary>Converts a string type to the EndpointType enum</summary>
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

    /// <summary>Logs information about a loaded endpoint with appropriate emoji based on type</summary>
    private static void LogEndpointLoading(string endpointName, EndpointDefinition definition)
    {
        if (definition.IsPrivate)
        {
            Log.Debug("Loaded private proxy endpoint: {Name} -> {Url}", endpointName, definition.Url);
        }
        else if (definition.IsComposite)
        {
            Log.Debug("Loaded composite proxy endpoint: {Name} -> {Url}", endpointName, definition.Url);
        }
        else if (definition.IsSql)
        {
            Log.Debug("Loaded SQL endpoint: {Name} -> {ObjectName}", endpointName, definition.DatabaseObjectName);
        }
        else
        {
            Log.Debug("Loaded proxy endpoint: {Name} -> {Url}", endpointName, definition.Url);
        }
    }

    /// <summary>Creates sample endpoint definitions if none exist</summary>
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

            Log.Information("Created sample endpoints in each endpoint directory");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error creating sample endpoint definitions");
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
            Log.Information($"Created sample SQL endpoint definition: {samplePath}");
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
            Log.Information($"Created sample proxy endpoint definition: {samplePath}");
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
            Log.Information($"Created sample composite endpoint definition: {compositeSamplePath}");
        }
    }

    private static void CreateSampleWebhookEndpoint(string webhookDir)
    {
        var sampleDir = Path.Combine(webhookDir, "Sample");
        if (!Directory.Exists(sampleDir))
        {
            Directory.CreateDirectory(sampleDir);
        }

        var samplePath = Path.Combine(sampleDir, "entity.json");
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
            Log.Information($"Created sample webhook endpoint definition: {samplePath}");
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
            Log.Information($"Created sample Files endpoint definition: {samplePath}");
        }
    }

    /// <summary>Reloads all endpoint definitions from disk</summary>
    public static void ReloadAllEndpoints()
    {
        lock (_loadLock)
        {
            _loadedProxyEndpoints = null;
            _loadedSqlEndpoints = null;
            _loadedSqlWebhookEndpoints = null;
            _loadedFileEndpoints = null;
            _loadedStaticEndpoints = null;

            Log.Information("All endpoint caches cleared, will reload on next access");
        }
    }

    /// <summary>Reloads a specific endpoint type</summary>
    public static void ReloadEndpointType(EndpointType type)
    {
        lock (_loadLock)
        {
            switch (type)
            {
                case EndpointType.SQL:
                    // Reload immediately so singletons get updated data
                    _loadedSqlEndpoints = ReloadSqlEndpoints();
                    _loadedSqlWebhookEndpoints = null; // Webhooks will be reloaded on next access
                    Log.Information("SQL endpoints reloaded from disk");
                    break;
                case EndpointType.Webhook:
                    _loadedSqlWebhookEndpoints = ReloadWebhookEndpoints();
                    Log.Information("Webhook endpoints reloaded from disk");
                    break;
                case EndpointType.Proxy:
                case EndpointType.Composite:
                    _loadedProxyEndpoints = ReloadProxyEndpoints();
                    Log.Information("Proxy endpoints reloaded from disk");
                    break;
                case EndpointType.Files:
                    _loadedFileEndpoints = ReloadFileEndpoints();
                    Log.Information("File endpoints reloaded from disk");
                    break;
                case EndpointType.Static:
                    _loadedStaticEndpoints = ReloadStaticEndpoints();
                    Log.Information("Static endpoints reloaded from disk");
                    break;
                default:
                    Log.Warning("Unknown endpoint type for reload: {Type}", type);
                    break;
            }
        }
    }

    /// <summary>Forces immediate reload of SQL endpoints</summary>
    private static Dictionary<string, EndpointDefinition> ReloadSqlEndpoints()
    {
        var sqlEndpointsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "endpoints", "SQL");
        return LoadSqlEndpoints(sqlEndpointsDirectory);
    }

    /// <summary>Forces immediate reload of proxy endpoints</summary>
    private static Dictionary<string, EndpointDefinition> ReloadProxyEndpoints()
    {
        var proxyEndpointsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "endpoints", "Proxy");
        return LoadProxyEndpoints(proxyEndpointsDirectory);
    }

    /// <summary>Forces immediate reload of file endpoints</summary>
    private static Dictionary<string, EndpointDefinition> ReloadFileEndpoints()
    {
        var fileEndpointsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "endpoints", "Files");
        return LoadFileEndpoints(fileEndpointsDirectory);
    }

    /// <summary>Forces immediate reload of webhook endpoints</summary>
    private static Dictionary<string, EndpointDefinition> ReloadWebhookEndpoints()
    {
        var webhookEndpointsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "endpoints", "Webhooks");
        return LoadSqlWebhookEndpoints(webhookEndpointsDirectory);
    }

    /// <summary>Forces immediate reload of static endpoints</summary>
    private static Dictionary<string, EndpointDefinition> ReloadStaticEndpoints()
    {
        var staticEndpointsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "endpoints", "Static");
        return LoadStaticEndpoints(staticEndpointsDirectory);
    }

    /// <summary>Extracts endpoint type from file path</summary>
    public static EndpointType? GetEndpointTypeFromPath(string filePath)
    {
        if (filePath.Contains("endpoints/SQL", StringComparison.OrdinalIgnoreCase) ||
            filePath.Contains("endpoints\\SQL", StringComparison.OrdinalIgnoreCase))
            return EndpointType.SQL;

        if (filePath.Contains("endpoints/Proxy", StringComparison.OrdinalIgnoreCase) ||
            filePath.Contains("endpoints\\Proxy", StringComparison.OrdinalIgnoreCase))
            return EndpointType.Proxy;

        if (filePath.Contains("endpoints/Webhooks", StringComparison.OrdinalIgnoreCase) ||
            filePath.Contains("endpoints\\Webhooks", StringComparison.OrdinalIgnoreCase))
            return EndpointType.Webhook;

        if (filePath.Contains("endpoints/Files", StringComparison.OrdinalIgnoreCase) ||
            filePath.Contains("endpoints\\Files", StringComparison.OrdinalIgnoreCase))
            return EndpointType.Files;

        if (filePath.Contains("endpoints/Static", StringComparison.OrdinalIgnoreCase) ||
            filePath.Contains("endpoints\\Static", StringComparison.OrdinalIgnoreCase))
            return EndpointType.Static;

        return null;
    }
}