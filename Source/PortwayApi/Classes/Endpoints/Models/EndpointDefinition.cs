namespace PortwayApi.Classes;

/// <summary>
/// Unified endpoint definition that handles all endpoint types
/// </summary>
public class EndpointDefinition
{
    public string Url { get; set; } = string.Empty;
    public List<string>? FallbackUrls { get; set; }
    public ProxyRetryOptions? Retry { get; set; }
    public ProxyResponseTransforms? ResponseTransforms { get; set; }
    public List<string> Methods { get; set; } = new List<string>();
    /// <summary>
    /// True when the proxied service understands OData query parameters; Portway itself only forwards them
    /// </summary>
    public bool SupportsOData { get; set; } = false;
    public EndpointType Type { get; set; } = EndpointType.Standard;
    public CompositeDefinition? CompositeConfig { get; set; }
    /// <summary>
    /// When false the endpoint returns 503 instead of serving requests
    /// </summary>
    public bool Enabled { get; set; } = true;
    public bool Hidden { get; set; } = false;
    /// <summary>
    /// Marks the endpoint's operations as deprecated in the OpenAPI document
    /// </summary>
    public bool Deprecated { get; set; } = false;

    /// <summary>
    /// Sent as the Deprecation header when Deprecated is true
    /// </summary>
    public DateTimeOffset? DeprecatedSince { get; set; }

    /// <summary>
    /// Sent as the Sunset header
    /// </summary>
    public DateTimeOffset? Sunset { get; set; }

    /// <summary>
    /// Version from a v{n} folder below the endpoint folder, null for the unversioned endpoint
    /// </summary>
    public string? Version { get; set; }

    public McpSettings? Mcp { get; set; }
    /// <summary>
    /// Computed from Mcp.Exposed for backward compatibility with tuple consumers.
    /// </summary>
    public bool IsMcpExposed => Mcp?.Exposed == true;

    // SQL endpoint properties
    public string? DatabaseObjectName { get; set; }
    public string? DatabaseSchema { get; set; }
    public List<string>? AllowedColumns { get; set; }
    public List<string>? RequiredColumns { get; set; }
    public Dictionary<string, ColumnValidationRule>? ColumnValidation { get; set; }
    public string? Procedure { get; set; }
    public string? PrimaryKey { get; set; }

    /// <summary>
    /// Write strategy: Procedure (default) or Table for generated parameterized statements
    /// </summary>
    public string? WriteMode { get; set; }

    /// <summary>
    /// True when this endpoint opts into direct table writes
    /// </summary>
    public bool UsesTableWrites => string.Equals(WriteMode, "Table", StringComparison.OrdinalIgnoreCase);

    public string? DatabaseObjectType { get; set; } = "Table"; // Table, View, TableValuedFunction
    public List<TVFParameter>? FunctionParameters { get; set; }

    /// <summary>
    /// To-one navigations exposed via OData $expand; empty for endpoints without $expand
    /// </summary>
    public List<EndpointRelationship>? Relationships { get; set; }

    // Column mappings lazy-load from AllowedColumns; holder reference makes publication atomic under concurrent reads
    private sealed record ColumnMappingSet(Dictionary<string, string> AliasToDatabase, Dictionary<string, string> DatabaseToAlias);
    private volatile ColumnMappingSet? _columnMappings;

    private ColumnMappingSet GetColumnMappings()
    {
        var mappings = _columnMappings;
        if (mappings == null)
        {
            var (aliasToDb, dbToAlias) = Helpers.ColumnMappingHelper.ParseColumnMappings(AllowedColumns);
            mappings = new ColumnMappingSet(aliasToDb, dbToAlias);
            _columnMappings = mappings;
        }
        return mappings;
    }

    public Dictionary<string, string> AliasToDatabase => GetColumnMappings().AliasToDatabase;

    public Dictionary<string, string> DatabaseToAlias => GetColumnMappings().DatabaseToAlias;

    // Environment restrictions
    public List<string>? AllowedEnvironments { get; set; }

    /// <summary>
    /// Inbound tenant header to its target column, parameter or upstream header; null when unset
    /// </summary>
    public IReadOnlyDictionary<string, string>? Tenancy { get; set; }

    public bool HasTenancy => Tenancy is { Count: > 0 };

    // File endpoint properties (optional, only for file endpoints)
    public Dictionary<string, object>? Properties { get; set; }

    // OpenAPI documentation properties
    public Documentation? Documentation { get; set; } // OpenAPI documentation settings

    // Custom properties for extended functionality
    public Dictionary<string, object>? CustomProperties { get; set; }

    // DELETE operation patterns
    public List<DeletePattern>? DeletePatterns { get; set; }

    /// <summary>
    /// Optional namespace for grouping related endpoints (e.g., "CRM", "Inventory") Takes precedence over folder-inferred namespace
    /// </summary>
    public string? Namespace { get; set; }

    /// <summary>
    /// Label for this endpoint (e.g., "Account Management") Shown as its tag title and in operation summaries, never part of a route or tag name
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Label for the namespace (e.g., "Customer Relationship Management") Shown as the namespace group title in the OpenAPI document, never part of a route or tag name
    /// </summary>
    public string? NamespaceDisplayName { get; set; }

    /// <summary>
    /// Markdown description of the namespace group in the OpenAPI document
    /// </summary>
    public string? NamespaceDescription { get; set; }

    /// <summary>
    /// Folder name where the endpoint definition is located
    /// </summary>
    public string? FolderName { get; set; }

    /// <summary>
    /// Folder holding this endpoint's entity.json, set by the loader
    /// </summary>
    public string? ConfigDirectory { get; set; }

    /// <summary>
    /// Namespace inferred from folder structure (for internal use)
    /// </summary>
    public string? InferredNamespace { get; set; }

    // Helper properties to simplify type checking
    public bool IsStandard => Type == EndpointType.Standard && !Hidden;
    public bool IsComposite => Type == EndpointType.Composite ||
                              (CompositeConfig != null && !string.IsNullOrEmpty(CompositeConfig.Name));
    public bool IsSql => Type == EndpointType.SQL;
    public bool IsStatic => Type == EndpointType.Static;

    // Namespace helper properties
    /// <summary>
    /// Gets the effective namespace (explicit namespace takes precedence over inferred)
    /// </summary>
    public string? EffectiveNamespace => Namespace ?? InferredNamespace;

    /// <summary>
    /// Indicates if this endpoint has a namespace (explicit or inferred)
    /// </summary>
    public bool HasNamespace => !string.IsNullOrEmpty(EffectiveNamespace);

    /// <summary>
    /// Gets the endpoint name for URL and key generation
    /// </summary>
    public string EndpointName => FolderName ?? (IsSql ? DatabaseObjectName : Path.GetFileNameWithoutExtension(Url)) ?? "Unknown";

    /// <summary>
    /// Gets the full path including namespace (for routing keys)
    /// </summary>
    public string FullPath => HasNamespace ? $"{EffectiveNamespace}/{EndpointName}" : EndpointName;

    /// <summary>
    /// Routing and scope key: FullPath plus @v{n} for a versioned endpoint
    /// </summary>
    public string Identity => FullPath + EndpointVersion.Suffix(Version);

    /// <summary>
    /// Path below /api/{env}: v{n}/ plus FullPath for a versioned endpoint
    /// </summary>
    public string RoutePath => EndpointVersion.RoutePrefix(Version) + FullPath;

    /// <summary>
    /// Creates URL patterns for routing (supports both namespaced and non-namespaced)
    /// </summary>
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

    /// <summary>
    /// Validates namespace naming conventions
    /// </summary>
    public List<string> ValidateNamespace()
    {
        var errors = new List<string>();
        var namespaceToCheck = EffectiveNamespace;

        if (!string.IsNullOrEmpty(namespaceToCheck))
        {
            // Nesting is expressed with slashes, so every segment is validated on its own
            var segments = namespaceToCheck.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var reserved = new[] { "api", "docs", "openapi", "health", "admin", "system", "composite", "webhook", "files" };

            if (segments.Length == 0)
            {
                errors.Add("Namespace cannot consist only of separators");
            }

            foreach (var segment in segments)
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(segment, @"^[A-Za-z][A-Za-z0-9_]*$"))
                {
                    errors.Add($"Namespace segment '{segment}' must start with a letter and contain only letters, numbers, and underscores");
                }

                if (reserved.Contains(segment.ToLowerInvariant()))
                {
                    errors.Add($"'{segment}' is a reserved namespace name");
                }
            }

            if (namespaceToCheck.Length > 50)
            {
                errors.Add("Namespace cannot exceed 50 characters");
            }
        }

        return errors;
    }

    /// <summary>
    /// Converts EndpointDefinition to the ProxyEndpointInfo snapshot used by composite and MCP consumers
    /// </summary>
    public ProxyEndpointInfo ToProxyEndpointInfo()
    {
        return new ProxyEndpointInfo(
            Url,
            new HashSet<string>(Methods, StringComparer.OrdinalIgnoreCase),
            Hidden,
            Enabled,
            Deprecated,
            IsMcpExposed,
            Type.ToString(),
            AllowedEnvironments,
            FallbackUrls,
            Retry,
            ResponseTransforms,
            HasTenancy
        );
    }
}
