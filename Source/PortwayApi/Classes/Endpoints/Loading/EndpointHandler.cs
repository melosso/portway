namespace PortwayApi.Classes;

using PortwayApi.Helpers;
using System.Collections.Frozen;
using System.Text.Json;
using Serilog;

public static partial class EndpointHandler
{
    // every type in one record so a reload publishes all of them in one write
    private sealed record EndpointSnapshot(FrozenDictionary<string, EndpointDefinition>[] Scanned, FrozenDictionary<string, EndpointDefinition>[] Routed);
    private static volatile EndpointSnapshot? _snapshot;
    private static readonly object _loadLock = new();

    private static readonly EndpointLoaderSpec ProxyLoaderSpec = new(
        "Proxy", "proxy", "", "*.json", NamespaceAware: true,
        ParseProxyEndpointDefinition,
        d => !string.IsNullOrWhiteSpace(d.Url) && d.Methods.Any(),
        "Url and at least one method are required",
        LogEndpointLoading);

    private static readonly EndpointLoaderSpec SqlLoaderSpec = new(
        "SQL", "SQL", "SQL ", "*.json", NamespaceAware: true,
        ParseSqlEndpointDefinition,
        d => !string.IsNullOrWhiteSpace(d.DatabaseObjectName),
        "DatabaseObjectName is required",
        (key, d) => Log.Debug($"SQL Endpoint: {key}; Object: {d.DatabaseSchema}.{d.DatabaseObjectName}; Namespace: {d.EffectiveNamespace ?? "None"}"));

    private static readonly EndpointLoaderSpec StaticLoaderSpec = new(
        "Static", "static", "static ", "entity.json", NamespaceAware: true,
        ParseStaticEndpointDefinition,
        _ => true,
        "The endpoint file is empty",
        (key, d) => Log.Debug("Static Endpoint: {Name} ({Hidden}) - {ContentType} | FullPath: {FullPath} | Namespace: {Namespace} | InferredNamespace: {InferredNamespace}",
            key,
            d.Hidden ? "Private" : "Public",
            d.Properties?.GetValueOrDefault("ContentType", "unknown"),
            d.FullPath,
            d.Namespace ?? "null",
            d.InferredNamespace ?? "null"));

    private static readonly EndpointLoaderSpec FileLoaderSpec = new(
        "File", "file", "file ", "*.json", NamespaceAware: true,
        ParseFileEndpointDefinition,
        _ => true,
        "The endpoint file is empty",
        (key, d) => Log.Debug("File Endpoint: {Name} ({Hidden}) | Namespace: {Namespace}",
            key, d.Hidden ? "Private" : "Public", d.EffectiveNamespace ?? "None"));

    private static readonly EndpointLoaderSpec WebhookLoaderSpec = new(
        "Webhook", "webhook", "webhook ", "entity.json", NamespaceAware: true,
        ParseWebhookEndpointDefinition,
        d => !string.IsNullOrWhiteSpace(d.DatabaseObjectName),
        "DatabaseObjectName is required",
        (key, d) =>
        {
            Log.Debug("Webhook Endpoint: {Name}; Object: {Schema}.{Object}; Namespace: {Namespace}",
                key, d.DatabaseSchema, d.DatabaseObjectName, d.EffectiveNamespace ?? "None");
            foreach (var (id, rule) in d.Verification ?? new Dictionary<string, Services.WebhookRule>())
            {
                if (rule.Error is not null)
                    Log.Error("Webhook {Endpoint} id {Id} verification is invalid and answers 401: {Error}", key, id, rule.Error);
                if (rule.Warning is not null)
                    Log.Warning("Webhook {Endpoint} id {Id} verification {Warning}", key, id, rule.Warning);
            }
        });

    // the order a name resolves in, a later type skips names an earlier type uses
    internal static readonly (EndpointType Type, string Folder, EndpointLoaderSpec Spec)[] RoutingOrder =
    [
        (EndpointType.SQL, "SQL", SqlLoaderSpec),
        (EndpointType.Webhook, "Webhooks", WebhookLoaderSpec),
        (EndpointType.Proxy, "Proxy", ProxyLoaderSpec),
        (EndpointType.Files, "Files", FileLoaderSpec),
        (EndpointType.Static, "Static", StaticLoaderSpec),
    ];

    /// <summary>
    /// Load errors for an endpoint file
    /// </summary>
    public static IReadOnlyList<string> ValidateConfig(EndpointType type, string json, string? file = null, string? endpointsDirectory = null) =>
        CheckConfig(type, json, file, endpointsDirectory).Errors;

    internal static (IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings) CheckConfig(EndpointType type, string json, string? file = null, string? endpointsDirectory = null)
    {
        var spec = type switch
        {
            EndpointType.SQL => SqlLoaderSpec,
            EndpointType.Proxy or EndpointType.Composite or EndpointType.Standard => ProxyLoaderSpec,
            EndpointType.Files => FileLoaderSpec,
            EndpointType.Static => StaticLoaderSpec,
            EndpointType.Webhook => WebhookLoaderSpec,
            _ => null
        };
        if (spec is null)
            return ([], []);

        try
        {
            var (key, definition, errors, metadataErrors) = EndpointDirectoryLoader.Evaluate(
                file is not null && endpointsDirectory is not null ? file : null, json, endpointsDirectory ?? "", spec);
            errors.AddRange(metadataErrors);
            if (endpointsDirectory is not null && key is not null && NameOwner(key, type) is { } owner)
                errors.Add($"This name is already used by the {owner} endpoint {key}. Pick another name so both stay reachable.");
            if (type == EndpointType.Webhook)
                errors.AddRange((definition?.AllowedColumns ?? []).Where(UnroutableWebhookId).Select(id => $"AllowedColumns id '{id}' cannot be used in a URL; remove spaces, slashes, quotes, brackets, ? # and %"));
            var warnings = new List<string>();
            foreach (var (id, rule) in definition?.Verification ?? new Dictionary<string, Services.WebhookRule>())
            {
                if (rule.Error is not null)
                    errors.Add($"Verification.{id}: {rule.Error}");
                if (rule.Warning is not null)
                    warnings.Add($"Verification.{id}: {rule.Warning}");
            }
            if (definition is { UsesTableWrites: true } && SqlTableWriteBuilder.ValidateConfig(definition) is { } tableWriteError)
                warnings.Add($"WriteMode: {tableWriteError}. The endpoint refuses writes");
            return (errors, warnings);
        }
        catch (JsonException ex)
        {
            return ([ex.Message], []);
        }
    }

    internal static bool UnroutableWebhookId(string id) =>
        id.Length == 0 || id.Any(c => char.IsWhiteSpace(c) || "/\\()'\"?#%".Contains(c));

    // Resolved once; accessors read it per lookup and GetCurrentDirectory is a syscall
    private static readonly string EndpointsBasePath = Path.Combine(Directory.GetCurrentDirectory(), "endpoints");

    /// <summary>
    /// Resolves the endpoints folder path, supporting both "Endpoints" and "endpoints" for cross-platform compatibility
    /// </summary>
    private static string GetEndpointsBasePath() => EndpointsBasePath;

    public static IReadOnlyDictionary<string, EndpointDefinition> GetSqlEndpoints() => EndpointsOf(EndpointType.SQL);

    public static IReadOnlyDictionary<string, EndpointDefinition> GetSqlWebhookEndpoints() => EndpointsOf(EndpointType.Webhook);

    public static IReadOnlyDictionary<string, EndpointDefinition> GetProxyEndpoints() => EndpointsOf(EndpointType.Proxy);

    public static IReadOnlyDictionary<string, EndpointDefinition> GetFileEndpoints() => EndpointsOf(EndpointType.Files);

    public static IReadOnlyDictionary<string, EndpointDefinition> GetStaticEndpoints() => EndpointsOf(EndpointType.Static);

    /// <summary>
    /// Gets all composite endpoint definitions from the endpoints directory
    /// </summary>
    public static Dictionary<string, CompositeDefinition> GetCompositeDefinitions(Dictionary<string, ProxyEndpointInfo> endpointMap)
    {
        var compositeDefinitions = new Dictionary<string, CompositeDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in GetProxyEndpoints())
        {
            if (kvp.Value.IsComposite && kvp.Value.CompositeConfig != null)
            {
                compositeDefinitions[kvp.Key] = kvp.Value.CompositeConfig;
            }
        }

        return compositeDefinitions;
    }

    /// <summary>
    /// Scans a directory for endpoint definitions keyed by endpoint name
    /// </summary>
    public static Dictionary<string, ProxyEndpointInfo> GetEndpoints(string endpointsDirectory)
    {
        var endpointMap = new Dictionary<string, ProxyEndpointInfo>(StringComparer.OrdinalIgnoreCase);
        if (!endpointsDirectory.Contains("Proxy", StringComparison.OrdinalIgnoreCase))
            return endpointMap;

        // legacy format, includes AllowedEnvironments for composite step validation
        foreach (var kvp in GetProxyEndpoints())
        {
            endpointMap[kvp.Key] = kvp.Value.ToProxyEndpointInfo();
        }

        return endpointMap;
    }

    /// <summary>
    /// The folder below endpoints a type is read from; a composite is read from the proxy folder
    /// </summary>
    internal static string FolderOf(EndpointType type) => RoutingOrder[RoutingIndex(type)].Folder;

    internal static string DirectoryOf(EndpointType type) => Path.Combine(EndpointsBasePath, FolderOf(type));

    private static int RoutingIndex(EndpointType type) =>
        Array.FindIndex(RoutingOrder, t => t.Type == (type is EndpointType.Composite ? EndpointType.Proxy : type));

    /// <summary>
    /// Endpoints of a type as routing serves them; a composite is a proxy endpoint
    /// </summary>
    internal static IReadOnlyDictionary<string, EndpointDefinition> EndpointsOf(EndpointType type) =>
        RoutingIndex(type) is var i and >= 0 ? Snapshot.Routed[i] : FrozenDictionary<string, EndpointDefinition>.Empty;

    /// <summary>
    /// The type routing serves a name with, read from one snapshot
    /// </summary>
    internal static bool TryFindByName(string key, out EndpointType type)
    {
        var routed = Snapshot.Routed;
        for (var i = 0; i < routed.Length; i++)
        {
            if (!routed[i].TryGetValue(key, out var found)) continue;
            type = RoutingOrder[i].Type == EndpointType.Proxy && found.IsComposite ? EndpointType.Composite : RoutingOrder[i].Type;
            return true;
        }

        type = EndpointType.Standard;
        return false;
    }

    /// <summary>
    /// The label of another endpoint type that uses a name, or null when none does
    /// </summary>
    internal static string? NameOwner(string key, EndpointType type)
    {
        var own = RoutingIndex(type is EndpointType.Standard ? EndpointType.Proxy : type);
        var routed = Snapshot.Routed;
        for (var i = 0; i < routed.Length; i++)
        {
            if (i != own && routed[i].ContainsKey(key))
                return RoutingOrder[i].Spec.TypeLabel;
        }
        return null;
    }

    // readers take the published snapshot without the lock
    private static EndpointSnapshot Snapshot
    {
        get
        {
            if (_snapshot is { } current)
                return current;
            lock (_loadLock)
                return _snapshot ??= Route([.. RoutingOrder.Select(t => Scan(t, null))]);
        }
    }

    private static FrozenDictionary<string, EndpointDefinition> Scan((EndpointType Type, string Folder, EndpointLoaderSpec Spec) type, IReadOnlyDictionary<string, EndpointDefinition>? previous) =>
        EndpointDirectoryLoader.Load(Path.Combine(EndpointsBasePath, type.Folder), type.Spec, previous);

    /// <summary>
    /// Drops names an earlier type in the routing order already uses, since the resolver never reaches them
    /// </summary>
    private static EndpointSnapshot Route(FrozenDictionary<string, EndpointDefinition>[] scanned)
    {
        var routed = new FrozenDictionary<string, EndpointDefinition>[scanned.Length];
        for (var i = 0; i < scanned.Length; i++)
        {
            List<string>? skipped = null;
            foreach (var key in scanned[i].Keys)
            {
                var owner = Array.FindIndex(routed, 0, i, earlier => earlier.ContainsKey(key));
                if (owner < 0) continue;
                Log.Error("{Type} endpoint {Name} was skipped because the {Owner} endpoint already uses that name. Rename one to keep both reachable.",
                    RoutingOrder[i].Spec.TypeLabel, key, RoutingOrder[owner].Spec.TypeLabel);
                (skipped ??= []).Add(key);
            }

            routed[i] = skipped is null
                ? scanned[i]
                : scanned[i].Where(kv => !skipped.Contains(kv.Key)).ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        }
        return new EndpointSnapshot(scanned, routed);
    }

    /// <summary>
    /// Reloads all endpoint definitions from disk
    /// </summary>
    public static void ReloadAllEndpoints()
    {
        lock (_loadLock)
        {
            _snapshot = null;
            Log.Information("All endpoint caches cleared, will reload on next access");
        }
    }

    /// <summary>
    /// Rescans one endpoint type and publishes every type again in one write
    /// </summary>
    public static void ReloadEndpointType(EndpointType type)
    {
        lock (_loadLock)
        {
            var i = RoutingIndex(type);
            if (i < 0)
            {
                Log.Warning("Unknown endpoint type for reload: {Type}", type);
                return;
            }

            var current = Snapshot;
            var scanned = (FrozenDictionary<string, EndpointDefinition>[])current.Scanned.Clone();
            scanned[i] = Scan(RoutingOrder[i], current.Scanned[i]);
            if (type == EndpointType.SQL)
            {
                var webhooks = RoutingIndex(EndpointType.Webhook);
                scanned[webhooks] = Scan(RoutingOrder[webhooks], current.Scanned[webhooks]);
            }

            _snapshot = Route(scanned);
            Log.Information("{Type} endpoints reloaded from disk", RoutingOrder[i].Spec.TypeLabel);
        }
    }

    /// <summary>
    /// Extracts endpoint type from file path
    /// </summary>
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
