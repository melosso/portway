namespace PortwayApi.Classes;

using System.Text.Json;
using System.Text.Json.Nodes;
using Serilog;

/// <summary>
/// OpenAPI documentation settings for an endpoint
/// </summary>
public class Documentation
{
    /// <summary>
    /// Description for the OpenAPI tag (supports Markdown)
    /// </summary>
    public string? TagDescription { get; set; }
    
    /// <summary>
    /// Custom summaries per HTTP method for OpenAPI operation summaries (e.g., "GET": "Retrieve account data")
    /// </summary>
    public Dictionary<string, string>? MethodDescriptions { get; set; }
    
    /// <summary>
    /// Custom detailed documentation per HTTP method for OpenAPI operation descriptions (e.g., "GET": "You can retrieve **all items** in the catalog...")
    /// </summary>
    public Dictionary<string, string>? MethodDocumentation { get; set; }
}

/// <summary>
/// Represents an endpoint entity with extended support for composite operations
/// </summary>
public class ExtendedEndpointEntity
{
    public string Url { get; set; } = string.Empty;
    public List<string> Methods { get; set; } = new List<string>();
    public string Type { get; set; } = "Standard"; // "Standard" or "Composite"
    public CompositeDefinition? CompositeConfig { get; set; }
    public bool IsPrivate { get; set; } = false; // If true, endpoint won't be exposed in the API (documentation)
    public List<string>? AllowedEnvironments { get; set; } // List of environments that can access this endpoint
    public Documentation? Documentation { get; set; } // OpenAPI documentation settings
}

/// <summary>
/// Represents an endpoint entity with support for both proxy and SQL endpoints
/// </summary>
public class EndpointEntity
{
    // SQL endpoint properties
    public string? DatabaseObjectName { get; set; }
    public string? DatabaseSchema { get; set; }
    public List<string>? AllowedColumns { get; set; }
    public string? Procedure { get; set; }
    public List<string>? AllowedMethods { get; set; }
    public string? PrimaryKey { get; set; }
    
    // Proxy endpoint properties
    public string? Url { get; set; }
    public List<string>? Methods { get; set; }
    
    // Shared properties
    public bool IsPrivate { get; set; } = false;
    public string Type { get; set; } = "Standard"; // Standard, SQL, Composite
    public CompositeDefinition? CompositeConfig { get; set; }
    public List<string>? AllowedEnvironments { get; set; }
    
    // OpenAPI documentation properties
    public Documentation? Documentation { get; set; }
}

/// <summary>
/// Defines the types of endpoints supported by the API
/// </summary>
public enum EndpointType
{
    /// <summary>
    /// Standard endpoint (fallback)
    /// </summary>
    Standard,

    /// <summary>
    /// SQL database endpoint
    /// </summary>
    SQL,

    /// <summary>
    /// Proxy endpoint to forward requests to another service
    /// </summary>
    Proxy,

    /// <summary>
    /// Composite endpoint that combines multiple operations
    /// </summary>
    Composite,

    /// <summary>
    /// Webhook endpoint for receiving external events
    /// </summary>
    Webhook,

    /// <summary>
    /// Files endpoint for file storage and retrieval
    /// </summary>
    Files,

    /// <summary>
    /// Static endpoint for serving predefined content
    /// </summary>
    Static,

    /// <summary>
    /// Private endpoint (not publicly accessible)
    /// </summary>
    Private
}

/// <summary>
/// Represents a File endpoint entity for local file handling
/// </summary>
public class FileEndpointEntity
{
    /// <summary>
    /// Type of storage (Local, S3, etc.)
    /// </summary>
    public string StorageType { get; set; } = "Local";
    
    /// <summary>
    /// Base directory for this endpoint (relative to the root storage directory)
    /// </summary>
    public string? BaseDirectory { get; set; }
    
    /// <summary>
    /// List of allowed file extensions
    /// </summary>
    public List<string>? AllowedExtensions { get; set; }
    
    /// <summary>
    /// Whether this endpoint is private (not accessible via API)
    /// </summary>
    public bool IsPrivate { get; set; } = false;
    
    /// <summary>
    /// List of environments allowed to access this endpoint
    /// </summary>
    public List<string>? AllowedEnvironments { get; set; }
}

/// <summary>
/// Represents a Static endpoint entity for serving predefined content
/// </summary>
public class StaticEndpointEntity
{
    /// <summary>
    /// MIME type for the response (application/json, text/plain, image/png, etc.)
    /// </summary>
    public string ContentType { get; set; } = "text/plain";
    
    /// <summary>
    /// Filename containing the static content (relative to the endpoint directory)
    /// </summary>
    public string ContentFile { get; set; } = "content.txt";
    
    /// <summary>
    /// Whether OData filtering ($filter, $select, etc.) is enabled for this endpoint
    /// </summary>
    public bool EnableFiltering { get; set; } = false;
    
    /// <summary>
    /// Whether this endpoint is private (not accessible via API)
    /// </summary>
    public bool IsPrivate { get; set; } = false;
    
    /// <summary>
    /// List of environments allowed to access this endpoint
    /// </summary>
    public List<string>? AllowedEnvironments { get; set; }
    
    /// <summary>
    /// OpenAPI documentation for this endpoint
    /// </summary>
    public Documentation? Documentation { get; set; }
}