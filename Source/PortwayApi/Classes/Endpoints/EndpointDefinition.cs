namespace PortwayApi.Classes;

using System.Text.Json;
using Serilog;
using PortwayApi.Helpers;

/// <summary>Unified endpoint definition that handles all endpoint types</summary>
public class EndpointDefinition
{
    public string Url { get; set; } = string.Empty;
    public List<string>? FallbackUrls { get; set; }
    public ProxyRetryOptions? Retry { get; set; }
    public ProxyResponseTransforms? ResponseTransforms { get; set; }
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
    
    // Column mappings lazy-load from AllowedColumns; holder reference makes publication atomic under concurrent reads
    private sealed record ColumnMappingSet(Dictionary<string, string> AliasToDatabase, Dictionary<string, string> DatabaseToAlias);
    private volatile ColumnMappingSet? _columnMappings;

    private ColumnMappingSet GetColumnMappings()
    {
        var mappings = _columnMappings;
        if (mappings == null)
        {
            var (aliasToDb, dbToAlias) = PortwayApi.Helpers.ColumnMappingHelper.ParseColumnMappings(AllowedColumns);
            mappings = new ColumnMappingSet(aliasToDb, dbToAlias);
            _columnMappings = mappings;
        }
        return mappings;
    }

    public Dictionary<string, string> AliasToDatabase => GetColumnMappings().AliasToDatabase;

    public Dictionary<string, string> DatabaseToAlias => GetColumnMappings().DatabaseToAlias;
    
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
            AllowedEnvironments,
            FallbackUrls,
            Retry,
            ResponseTransforms
        );
    }
}
