namespace PortwayApi.Classes;

using System.Text.Json;
using Serilog;
using PortwayApi.Helpers;

public static partial class EndpointHandler
{
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
}
