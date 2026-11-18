namespace PortwayApi.Classes;

using System.Collections.Frozen;
using PortwayApi.Helpers;
using System.Text.Json;
using Serilog;

public static partial class EndpointHandler
{
    /// <summary>
    /// Parses a webhook endpoint definition; reuses the SQL parser and tags the result as a Webhook
    /// </summary>
    private static EndpointDefinition? ParseWebhookEndpointDefinition(string json)
    {
        var definition = ParseSqlEndpointDefinition(json);
        if (definition != null)
        {
            if (definition.AllowedColumns.Count == 0)
                throw new InvalidOperationException("Webhook endpoint requires AllowedColumns, the list of accepted webhook ids");

            definition.Type = EndpointType.Webhook;

            var entity = JsonSerializer.Deserialize<EndpointEntity>(json, SharedJsonOptions.CaseInsensitive);
            definition.Verification = BuildVerification(entity?.Verification, definition.AllowedColumns);
            if (definition.Hidden && definition.Verification?.Values.Any(r => r.AcceptForm) == true)
                throw new InvalidOperationException("AcceptForm is not allowed on a Hidden webhook endpoint");
        }
        return definition;
    }

    /// <summary>
    /// Internal method to load all file endpoints from the endpoints directory
    /// </summary>
    private static FrozenDictionary<string, EndpointDefinition> LoadFileEndpoints(string endpointsDirectory, IReadOnlyDictionary<string, EndpointDefinition>? previous = null)
        => EndpointDirectoryLoader.Load(endpointsDirectory, FileLoaderSpec, previous);

    /// <summary>
    /// Internal method to load all static endpoints from the endpoints directory
    /// </summary>
    private static FrozenDictionary<string, EndpointDefinition> LoadStaticEndpoints(string endpointsDirectory, IReadOnlyDictionary<string, EndpointDefinition>? previous = null)
        => EndpointDirectoryLoader.Load(endpointsDirectory, StaticLoaderSpec, previous);

    /// <summary>
    /// Parses a file endpoint definition from JSON
    /// </summary>
    private static EndpointDefinition? ParseFileEndpointDefinition(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        var entity = JsonSerializer.Deserialize<FileEndpointEntity>(json, SharedJsonOptions.CaseInsensitive);

        if (entity == null)
            return null;

        var definition = new EndpointDefinition
        {
            Type = EndpointType.Files,
            Methods = new List<string> { "GET", "POST", "DELETE" },
            // Store file-specific properties in Properties dictionary
            Properties = new Dictionary<string, object>
            {
                ["StorageType"] = entity.StorageType,
                ["BaseDirectory"] = entity.BaseDirectory ?? "",
                ["AllowedExtensions"] = entity.AllowedExtensions ?? new List<string>()
            }
        };

        entity.ApplyTo(definition);
        return definition;
    }

    /// <summary>
    /// Parses a static endpoint definition from JSON
    /// </summary>
    private static EndpointDefinition? ParseStaticEndpointDefinition(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        var entity = JsonSerializer.Deserialize<StaticEndpointEntity>(json, SharedJsonOptions.CaseInsensitive);

        if (entity == null)
            return null;

        var definition = new EndpointDefinition
        {
            Type = EndpointType.Static,
            Methods = new List<string> { "GET" },
            // Store static-specific properties in Properties dictionary
            Properties = new Dictionary<string, object>
            {
                ["ContentType"] = entity.ContentType,
                ["ContentFile"] = entity.ContentFile,
                ["EnableFiltering"] = entity.EnableFiltering
            }
        };

        entity.ApplyTo(definition);
        return definition;
    }

    public static IReadOnlyDictionary<string, EndpointDefinition> GetFileEndpoints()
    {
        string fileEndpointsDirectory = Path.Combine(GetEndpointsBasePath(), "Files");
        return LoadFileEndpointsIfNeeded(fileEndpointsDirectory);
    }

    /// <summary>
    /// Gets Static endpoints from the /endpoints/Static directory
    /// </summary>
    public static IReadOnlyDictionary<string, EndpointDefinition> GetStaticEndpoints()
    {
        string staticEndpointsDirectory = Path.Combine(GetEndpointsBasePath(), "Static");
        return LoadStaticEndpointsIfNeeded(staticEndpointsDirectory);
    }

    /// <summary>
    /// Parses a proxy endpoint definition from JSON, handling both legacy and extended formats
    /// </summary>
    internal static EndpointDefinition? ParseProxyEndpointDefinition(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        var entity = JsonSerializer.Deserialize<EndpointEntity>(json, SharedJsonOptions.CaseInsensitive);

        if (entity == null || string.IsNullOrWhiteSpace(entity.Url))
            return null;

        var errors = ValidateProxyEndpointConfiguration(entity);
        if (errors.Count > 0)
            throw new InvalidOperationException($"Endpoint configuration is invalid: {string.Join(", ", errors)}");

        var definition = new EndpointDefinition
        {
            Url = entity.Url,
            Headers = entity.Headers,
            Methods = entity.Methods ?? new List<string>(),
            Type = ParseEndpointType(entity.Type),
            CompositeConfig = entity.CompositeConfig,
            CustomProperties = entity.CustomProperties,
            DeletePatterns = entity.DeletePatterns,
            SupportsOData = entity.SupportsOData,
            FallbackUrls = entity.FallbackUrls,
            ForwardAuthorization = entity.ForwardAuthorization,
            Retry = entity.Retry,
            ResponseTransforms = entity.ResponseTransforms
        };

        entity.ApplyTo(definition);
        return definition;
    }

    internal static IReadOnlyDictionary<string, Services.WebhookRule>? BuildVerification(Dictionary<string, WebhookVerification>? verification, List<string> allowedColumns)
    {
        if (verification is null || verification.Count == 0)
            return null;

        var rules = new Dictionary<string, Services.WebhookRule>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, config) in verification)
        {
            var canonical = allowedColumns.FirstOrDefault(c => c.Equals(id, StringComparison.OrdinalIgnoreCase));
            var rule = Services.WebhookVerifier.Build(config);
            rules[canonical ?? id] = canonical is null ? rule with { Error = "id is not in AllowedColumns" } : rule;
        }

        foreach (var shared in rules.Where(r => r.Value.SecretName.Length > 0).GroupBy(r => r.Value.SecretName, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            foreach (var (id, rule) in shared.ToList())
                rules[id] = rule with { Warning = $"secret {shared.Key} is shared by ids {string.Join(", ", shared.Select(s => s.Key))}; use one secret per sender" };
        }
        return rules;
    }

    internal static List<string> ValidateProxyEndpointConfiguration(EndpointEntity entity)
    {
        var errors = new List<string>();
        foreach (var name in entity.Headers?.Keys ?? Enumerable.Empty<string>())
        {
            if (!HeaderPolicy.IsValidName(name) || HeaderPolicy.StrippedFromClient.Contains(name) || name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
                errors.Add($"Header '{name}' is not allowed");
        }

        foreach (var url in (IEnumerable<string?>)[entity.Url, .. entity.FallbackUrls ?? []])
        {
            if (url is not null && !SecretReference.IsQueryValueOnly(url))
                errors.Add("A secret reference in a URL is allowed in a query value only");
        }
        return errors;
    }

    /// <summary>
    /// Parses a SQL endpoint definition from JSON
    /// </summary>
    internal static EndpointDefinition? ParseSqlEndpointDefinition(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        var entity = JsonSerializer.Deserialize<EndpointEntity>(json, SharedJsonOptions.CaseInsensitive);

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

        var definition = new EndpointDefinition
        {
            Type = EndpointType.SQL,
            DatabaseObjectName = entity.DatabaseObjectName,
            DatabaseSchema = schema,
            AllowedColumns = entity.AllowedColumns ?? [],
            Procedure = entity.Procedure,
            PrimaryKey = entity.PrimaryKey,
            WriteMode = entity.WriteMode,
            DatabaseObjectType = entity.DatabaseObjectType ?? "Table",
            FunctionParameters = entity.FunctionParameters,
            Relationships = entity.Relationships,
            Methods = allowedMethods,
            Properties = entity.Properties,
            ResponseTransforms = entity.ResponseTransforms
        };

        entity.ApplyTo(definition);
        return definition;
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

            var tvfErrors = Handlers.TableValuedFunctionSqlHandler.ValidateTVFConfiguration(tempEndpoint);
            errors.AddRange(tvfErrors);
        }

        // Validate $expand relationship shape (identifiers, to-one only, not on a TVF)
        errors.AddRange(Helpers.OdataExpandRelationshipValidator.ValidateShape(entity));
        errors.AddRange(Services.Caching.CacheGenerationKeys.Validate(entity.Properties));

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
        if (definition.Hidden)
        {
            Log.Debug("Loaded private proxy endpoint: {Name} -> {Url}", endpointName, UrlRedaction.Redact(definition.Url));
        }
        else if (definition.IsComposite)
        {
            Log.Debug("Loaded composite proxy endpoint: {Name} -> {Url}", endpointName, UrlRedaction.Redact(definition.Url));
        }
        else if (definition.IsSql)
        {
            Log.Debug("Loaded SQL endpoint: {Name} -> {ObjectName}", endpointName, definition.DatabaseObjectName);
        }
        else
        {
            Log.Debug("Loaded proxy endpoint: {Name} -> {Url}", endpointName, UrlRedaction.Redact(definition.Url));
        }
    }
}
