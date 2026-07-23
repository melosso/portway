namespace PortwayApi.Classes;

using System.Text.Json;
using Serilog;
using PortwayApi.Helpers;


public static partial class EndpointHandler
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
