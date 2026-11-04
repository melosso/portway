namespace PortwayApi.Services.Configuration;

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PortwayApi.Helpers;
using Serilog;

/// <summary>
/// A setting the Web UI and API are allowed to change, with the validation that guards it
/// </summary>
public sealed record WritableSetting(
    string Key,
    string Kind,
    bool RequiresRestart,
    double? Min = null,
    double? Max = null,
    string[]? Choices = null);

/// <summary>
/// Outcome of a write attempt; Field names the setting that failed so a caller can mark it
/// </summary>
public sealed record SettingsWriteResult(bool Ok, string? Error = null, string? Field = null, bool RestartRequired = false);

/// <summary>
/// Writes allowlisted settings to appsettings.overrides.json, never appsettings.json
/// </summary>
public sealed class SettingsWriteService
{
    public const string OverridesFileName = "appsettings.overrides.json";

    private static readonly string[] LogLevels =
        ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"];

    private static readonly Regex ScheduleFormat = new(@"^([01]\d|2[0-3]):[0-5]\d$", RegexOptions.Compiled);

    private static readonly Regex NameFormat = new(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,49}$", RegexOptions.Compiled);

    private static readonly Regex NamespaceFormat = new(@"^[A-Za-z][A-Za-z0-9_.]{0,99}$", RegexOptions.Compiled);

    private static readonly Regex RelativePathFormat = new(@"^[A-Za-z0-9._-]+(/[A-Za-z0-9._-]+)*$", RegexOptions.Compiled);

    private static readonly Regex RouteFormat = new(@"^(/[a-z0-9][a-z0-9_-]*)+$", RegexOptions.Compiled);

    private static readonly Regex PathBaseFormat = new(@"^(/[A-Za-z0-9._-]+)*$", RegexOptions.Compiled);

    private static readonly Regex HostFormat = new(@"^(\*\.)?[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)*$", RegexOptions.Compiled);

    private static readonly Regex ExtensionFormat = new(@"^\.[A-Za-z0-9]{1,10}$", RegexOptions.Compiled);

    // Part of SQL connection strings
    private static readonly Regex ApplicationNameFormat = new(@"^[A-Za-z0-9 ._()-]{1,128}$", RegexOptions.Compiled);

    private static readonly Regex ResourceAttributesFormat = new(@"^([A-Za-z0-9._-]+=[^,=;\s]+(,[A-Za-z0-9._-]+=[^,=;\s]+)*)?$", RegexOptions.Compiled);

    private static readonly string[] ReservedFolders = ["endpoints", "environments", "tokens", "wwwroot", ".core", ".backups"];

    private static readonly string[] ProtectedFiles = ["auth.db", "mcp.db", "metrics.db"];

    private static readonly string[] ReservedRoutes = ["/api", "/docs", "/health", "/openapi"];

    private static readonly string[] ScalarThemes =
        ["portway", "default", "alternate", "moon", "purple", "solarized", "bluePlanet", "saturn", "kepler", "mars", "deepSpace", "elysiajs", "fastify", "laserwave", "none"];

    private static readonly string[] Providers = ["Memory", "Redis"];

    // Writable keys; anything else is refused by name
    private static readonly Dictionary<string, WritableSetting> Allowed = new WritableSetting[]
    {
        new("RateLimiting:Enabled", "bool", true),
        new("RateLimiting:IpLimit", "int", true, 1, 1_000_000),
        new("RateLimiting:IpWindowSeconds", "int", true, 1, 86_400),
        new("RateLimiting:TokenLimit", "int", true, 1, 1_000_000),
        new("RateLimiting:TokenWindowSeconds", "int", true, 1, 86_400),
        new("RateLimiting:Provider", "choice", true, Choices: Providers),
        new("RateLimiting:Redis:ConnectionString", "secret", true),

        new("Caching:Enabled", "bool", false),
        new("Caching:DefaultCacheDurationSeconds", "int", false, 0, 86_400),
        new("Caching:MemoryCacheSizeLimitMB", "int", true, 1, 8_192),
        new("Caching:Provider", "choice", true, Choices: Providers),
        new("Caching:EndpointCacheDurations", "intmap", false, 0, 86_400),
        new("Caching:CacheableContentTypes", "mediatypelist", false, Max: 50),
        new("Caching:Redis:ConnectionString", "secret", true),
        new("Caching:Redis:InstanceName", "text", true, Max: 100),
        new("Caching:Redis:Database", "int", true, 0, 255),
        new("Caching:Redis:UseSsl", "bool", true),
        new("Caching:Redis:ConnectTimeoutMs", "int", true, 100, 60_000),
        new("Caching:Redis:AbortOnConnectFail", "bool", true),
        new("Caching:Redis:FallbackToMemoryCache", "bool", true),
        new("Caching:Redis:MaxRetryAttempts", "int", true, 0, 10),
        new("Caching:Redis:RetryDelayMs", "int", true, 0, 60_000),

        new("SqlConnectionPooling:Enabled", "bool", true),
        new("SqlConnectionPooling:MinPoolSize", "int", true, 0, 1_000),
        new("SqlConnectionPooling:MaxPoolSize", "int", true, 1, 5_000),
        new("SqlConnectionPooling:ConnectionTimeoutSeconds", "int", true, 1, 600),
        new("SqlConnectionPooling:CommandTimeoutSeconds", "int", true, 1, 3_600),
        new("SqlConnectionPooling:ApplicationName", "appname", true),

        new("EndpointReloading:Enabled", "bool", false),
        new("EndpointReloading:DebounceMs", "int", false, 50, 60_000),

        new("Serilog:MinimumLevel:Default", "choice", true, Choices: LogLevels),
        new("Serilog:MinimumLevel:Override", "levelmap", true),

        new("RequestTrafficLogging:Enabled", "bool", true),
        new("RequestTrafficLogging:Provider", "choice", true, Choices: ["File", "Sqlite"]),
        new("RequestTrafficLogging:SqlitePath", "dbfile", true),
        new("RequestTrafficLogging:LogDirectory", "relpath", true),
        new("RequestTrafficLogging:FilePrefix", "name", true),
        new("RequestTrafficLogging:MaxFileSizeMB", "int", true, 1, 1_024),
        new("RequestTrafficLogging:MaxFileCount", "int", true, 1, 100),
        new("RequestTrafficLogging:QueueCapacity", "int", true, 100, 1_000_000),
        new("RequestTrafficLogging:BatchSize", "int", true, 1, 10_000),
        new("RequestTrafficLogging:FlushIntervalMs", "int", true, 100, 60_000),
        new("RequestTrafficLogging:CaptureHeaders", "bool", true),
        new("RequestTrafficLogging:CaptureRequestBodies", "bool", true),
        new("RequestTrafficLogging:CaptureResponseBodies", "bool", true),
        new("RequestTrafficLogging:MaxBodyCaptureSizeBytes", "int", true, 0, 1_048_576),
        new("RequestTrafficLogging:InfoLoggingEnabled", "bool", true),

        new("DatabaseMaintenance:Enabled", "bool", false),
        new("DatabaseMaintenance:Schedule", "time", false),
        new("DatabaseMaintenance:RunOnStartup", "bool", true),
        new("DatabaseMaintenance:FreePageRatioThreshold", "number", false, 0.01, 0.99),
        new("DatabaseMaintenance:Backup:Enabled", "bool", false),
        new("DatabaseMaintenance:Backup:Retention", "int", false, 1, 100),

        new("Mcp:Enabled", "bool", true),
        new("Mcp:Path", "route", true),
        new("Mcp:ChatEnabled", "bool", true),
        new("Mcp:ToolTimeoutSeconds", "int", true, 1, 600),
        new("Mcp:DefaultPageSize", "int", true, 1, 1_000),
        new("Mcp:MaxPageSize", "int", true, 1, 1_000),
        new("Mcp:MaxToolResultChars", "int", true, 1_000, 1_000_000),

        new("Console:SecureCookies", "bool", true),
        new("Console:Customization:LandingPageEnabled", "bool", true),
        new("Console:Customization:PromoText", "text", false, Max: 2_000),
        new("Console:Customization:PromoLogin", "bool", false),
        new("Console:Customization:LoginFooter", "text", false, Max: 2_000),
        new("Console:CorsOrigins", "originlist", true, Max: 50),

        new("Oidc:Enabled", "bool", false),
        new("OpenApi:Enabled", "bool", true),
        new("OpenApi:Title", "text", false, Max: 200),
        new("OpenApi:Version", "name", false),
        new("OpenApi:Description", "text", false, Max: 4_000),
        new("OpenApi:Contact:Name", "text", false, Max: 200),
        new("OpenApi:Contact:Email", "email", false),
        new("OpenApi:ForceHttpsInProduction", "bool", false),
        new("OpenApi:ShowNamespaces", "bool", false),
        new("OpenApi:DefaultGroup", "name", false),
        new("OpenApi:ShowBadges", "bool", false),
        new("OpenApi:ServeMarkdown", "bool", false),
        new("OpenApi:ExternalDocs:Url", "weburl", false),
        new("OpenApi:ExternalDocs:Description", "text", false, Max: 200),
        new("OpenApi:SecurityDefinition:Description", "text", true, Max: 500),
        new("OpenApi:Footer:Text", "text", false, Max: 200),
        new("OpenApi:Footer:Url", "link", false),
        new("OpenApi:Footer:Target", "choice", false, Choices: ["_blank", "_self"]),
        new("OpenApi:Footer:ShowSourceIcon", "bool", false),
        new("OpenApi:Scalar:Theme", "choice", false, Choices: ScalarThemes),
        new("OpenApi:Scalar:Layout", "choice", false, Choices: ["modern", "classic"]),
        new("OpenApi:Scalar:ShowSidebar", "bool", false),
        new("OpenApi:Scalar:HideDownloadButton", "bool", false),
        new("OpenApi:Scalar:HideModels", "bool", false),
        new("OpenApi:Scalar:HideClientButton", "bool", false),
        new("OpenApi:Scalar:HideTestRequestButton", "bool", false),

        new("Telemetry:Provider", "choice", true, Choices: ["None", "Otlp", "Prometheus"]),
        new("Telemetry:Otlp:Endpoint", "weburl", true),
        new("Telemetry:ServiceName", "name", true),
        new("Telemetry:ResourceAttributes", "attributes", true, Max: 1_000),
        new("Telemetry:Prometheus:Path", "route", true),

        new("FileStorage:StorageDirectory", "relpath", true),
        new("FileStorage:MaxFileSizeBytes", "int", false, 1_024, 1_073_741_824),
        new("FileStorage:UseMemoryCache", "bool", false),
        new("FileStorage:MemoryCacheDurationSeconds", "int", false, 1, 86_400),
        new("FileStorage:MaxTotalMemoryCacheMB", "int", false, 1, 8_192),
        new("FileStorage:AllowedExtensions", "extlist", false, Max: 200),
        new("FileStorage:BlockedExtensions", "extlist", false, Max: 200),

        new("Proxy:Username", "text", true, Max: 200),
        new("Proxy:Password", "secret", true),
        new("Proxy:Domain", "text", true, Max: 200),

        // Deployment shape; the endpoint refuses changes that lock the caller out
        new("Console:PublicOrigins", "originlist", true, Max: 50),
        new("ForwardedHeaders:KnownProxies", "iplist", true, Max: 50),
        new("ForwardedHeaders:KnownNetworks", "cidrlist", true, Max: 50),
        new("AllowedHosts", "hostlist", true, Max: 50),
        new("PathBase", "pathbase", true),

        // Credentials: clear only, never set
        new("Console:AdminApiKey", "clear", true),
        new("Console:SeedPassword", "clear", true),
        new("Console:SeedToken", "clear", true),
        new("Console:Enabled", "bool", true),
    }.ToDictionary(s => s.Key, StringComparer.OrdinalIgnoreCase);

    private static readonly SemaphoreSlim WriteLock = new(1, 1);

    private readonly IReadOnlyList<string> _envNames;

    private readonly IConfigurationRoot? _root;

    public SettingsWriteService(IConfiguration configuration)
        : this([.. Environment.GetEnvironmentVariables().Keys.Cast<string>()], configuration as IConfigurationRoot) { }

    internal SettingsWriteService(IReadOnlyList<string> envNames, IConfigurationRoot? root = null)
    {
        _envNames = envNames;
        _root = root;
    }

    /// <summary>
    /// Locking env variable, or null.
    /// </summary>
    public string? LockedBy(string key) =>
        EnvAliases.EnvNameFor(key, _envNames) ??
        (ConfigAliases.Deprecated(key) is { } deprecated ? EnvAliases.EnvNameFor(deprecated, _envNames) : null);

    public static IReadOnlyCollection<WritableSetting> Schema => Allowed.Values;

    /// <summary>
    /// Collections replace lower layers.
    /// </summary>
    public static bool IsCollection(string kind) =>
        kind is "originlist" or "iplist" or "cidrlist" or "extlist" or "mediatypelist" or "intmap" or "levelmap";

    public static string OverridesPath => Path.Combine(Directory.GetCurrentDirectory(), OverridesFileName);

    /// <summary>
    /// Creates an empty overrides file so the first write reloads without a restart
    /// </summary>
    public static void EnsureExists()
    {
        try
        {
            if (!File.Exists(OverridesPath)) File.WriteAllText(OverridesPath, "{}" + Environment.NewLine);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not create {File}; settings written from the Web UI will need a restart", OverridesFileName);
        }
    }

    /// <summary>
    /// Validates every entry, then writes them together; nothing is applied when one fails
    /// </summary>
    public async Task<SettingsWriteResult> ApplyAsync(IDictionary<string, JsonElement> changes)
    {
        if (changes.Count == 0)
            return new SettingsWriteResult(false, "No settings supplied");

        var parsed = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        var restart = false;

        foreach (var (key, raw) in changes)
        {
            if (!Allowed.TryGetValue(key, out var spec))
                return new SettingsWriteResult(false, $"'{key}' is not a writable setting", key);

            if (LockedBy(spec.Key) is { } envName)
                return new SettingsWriteResult(false, $"'{spec.Key}' is set by {envName}", spec.Key);

            if (spec.Kind == "secret" && raw.ValueKind == JsonValueKind.String && raw.GetString() is { Length: > 0 } && !SettingsEncryptionHelper.HasKeyPair)
                return new SettingsWriteResult(false, "Encryption keys are not initialised; restart Portway and retry", spec.Key);

            var (node, error) = Coerce(spec, raw);
            if (error is not null)
                return new SettingsWriteResult(false, error, key);

            parsed[spec.Key] = node;
            restart |= spec.RequiresRestart;
        }

        // the legacy Enabled switch selects Otlp when no provider is set
        if (parsed.ContainsKey("Telemetry:Provider"))
        {
            if (LockedBy("Telemetry:Enabled") is { } legacyEnv)
                return new SettingsWriteResult(false, $"'Telemetry:Provider' is set by {legacyEnv}", "Telemetry:Provider");
            parsed["Telemetry:Enabled"] = JsonValue.Create(false);
        }

        if (CrossCheck(parsed) is { } crossError)
            return new SettingsWriteResult(false, crossError.Message, crossError.Field);

        await WriteLock.WaitAsync();
        try
        {
            var root = await ReadOverridesAsync();
            MigrateDeprecatedKeys(root);
            foreach (var (key, node) in parsed) SetPath(root, key, node);
            await WriteOverridesAsync(root);
            // file watchers miss in-place writes on a single-file bind mount
            _root?.Reload();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to write {File}", OverridesFileName);
            return new SettingsWriteResult(false, "Could not write the settings file");
        }
        finally
        {
            WriteLock.Release();
        }

        return new SettingsWriteResult(true, RestartRequired: restart);
    }

    /// <summary>
    /// Validates one value.
    /// </summary>
    internal static (JsonNode? Node, string? Error) Coerce(string key, JsonElement raw) =>
        Allowed.TryGetValue(key, out var spec) ? Coerce(spec, raw) : (null, $"'{key}' is not a writable setting");

    private static (JsonNode? Node, string? Error) Coerce(WritableSetting spec, JsonElement raw) => spec.Kind switch
    {
        "bool" => raw.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? (JsonValue.Create(raw.GetBoolean()), null)
            : (null, $"'{spec.Key}' expects true or false"),

        "int" => ReadInt(spec, raw),

        "number" => ReadNumber(spec, raw),

        "choice" => raw.ValueKind == JsonValueKind.String
                    && spec.Choices!.Contains(raw.GetString(), StringComparer.OrdinalIgnoreCase)
            ? (JsonValue.Create(spec.Choices!.First(c => c.Equals(raw.GetString(), StringComparison.OrdinalIgnoreCase))), null)
            : (null, $"'{spec.Key}' must be one of {string.Join(", ", spec.Choices!)}"),

        // Free text bounded by Max; null clears the override back to whatever appsettings.json declares
        "text" => ReadText(spec, raw),

        "iplist" => ReadList(spec, raw, ParseIp),
        "cidrlist" => ReadList(spec, raw, ParseNetwork),
        "originlist" => ReadList(spec, raw, ParseOrigin),
        "extlist" => ReadList(spec, raw, ParseExtension),
        "mediatypelist" => ReadList(spec, raw, ParseMediaType),

        "intmap" => ReadMap(spec, raw, NameFormat, value =>
            value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n) && n >= spec.Min && n <= spec.Max
                ? JsonValue.Create(n)
                : null, $"a whole number from {spec.Min:0} to {spec.Max:0}"),

        "levelmap" => ReadMap(spec, raw, NamespaceFormat, value =>
            value.ValueKind == JsonValueKind.String && LogLevels.FirstOrDefault(l => l.Equals(value.GetString(), StringComparison.OrdinalIgnoreCase)) is { } level
                ? JsonValue.Create(level)
                : null, $"one of {string.Join(", ", LogLevels)}"),

        // HostFiltering expects one string
        "hostlist" => ReadList(spec, raw, ParseHost) switch
        {
            (JsonArray { Count: 0 }, null) => (null, $"'{spec.Key}' needs at least one host, or *"),
            (JsonArray hosts, null) => (JsonValue.Create(string.Join(';', hosts.Select(h => h!.GetValue<string>()))), null),
            var failed => failed
        },

        // Only ever accepts the empty string: a credential may be removed here, never introduced
        "clear" => raw.ValueKind == JsonValueKind.String && raw.GetString() == ""
            ? (JsonValue.Create(""), null)
            : (null, $"'{spec.Key}' can only be cleared from here, not set"),

        // Encrypted; empty clears
        "secret" => raw.ValueKind switch
        {
            JsonValueKind.Null => (null, null),
            JsonValueKind.String when raw.GetString() is "" => (null, null),
            JsonValueKind.String when raw.GetString() is { Length: <= 1_024 } secret && !secret.Any(char.IsControl)
                => (JsonValue.Create(SettingsEncryptionHelper.Encrypt(secret)), null),
            _ => (null, $"'{spec.Key}' must be text of 1024 characters or fewer")
        },

        // identifiers that land in routes, tag names and the docs page; empty restores the default
        "name" => raw.ValueKind == JsonValueKind.String && (raw.GetString() is "" || NameFormat.IsMatch(raw.GetString()!))
            ? (JsonValue.Create(raw.GetString()), null)
            : (null, $"'{spec.Key}' allows letters, digits, dots, dashes and underscores only"),

        "appname" => ReadString(spec, raw, value => ApplicationNameFormat.IsMatch(value)
            ? null
            : $"'{spec.Key}' allows letters, digits, spaces, dots, dashes, underscores and parentheses, up to 128 characters"),

        "attributes" => ReadString(spec, raw, value => value.Length <= spec.Max && ResourceAttributesFormat.IsMatch(value)
            ? null
            : $"'{spec.Key}' must be key=value pairs separated by commas"),

        "relpath" => ReadString(spec, raw, ParseRelativePath),

        "dbfile" => ReadString(spec, raw, value => ParseRelativePath(value) ??
            (value.EndsWith(".db", StringComparison.OrdinalIgnoreCase) ? null : $"'{value}' must end in .db")),

        "route" => ReadString(spec, raw, ParseRoute),

        "pathbase" => ReadString(spec, raw, value =>
            value.Length <= 100 && PathBaseFormat.IsMatch(value) && !value.Split('/').Any(s => s is "." or "..")
                ? null
                : $"'{value}' must be empty or a path such as /portway"),

        "link" => raw.ValueKind == JsonValueKind.String && IsSafeLink(raw.GetString()!)
            ? (JsonValue.Create(raw.GetString()), null)
            : (null, $"'{spec.Key}' must be an http, https or mailto link, or #"),

        "weburl" => raw.ValueKind == JsonValueKind.String && (raw.GetString() is "" || Uri.TryCreate(raw.GetString(), UriKind.Absolute, out var web) && web.Scheme is "http" or "https")
            ? (JsonValue.Create(raw.GetString()), null)
            : (null, $"'{spec.Key}' must be an http or https link, or empty"),

        "email" => raw.ValueKind == JsonValueKind.String && (raw.GetString() is "" || IsEmail(raw.GetString()!))
            ? (JsonValue.Create(raw.GetString()), null)
            : (null, $"'{spec.Key}' must be an e-mail address"),

        "time" => raw.ValueKind == JsonValueKind.String && ScheduleFormat.IsMatch(raw.GetString() ?? "")
            ? (JsonValue.Create(raw.GetString()), null)
            : (null, $"'{spec.Key}' must be a 24-hour time such as 03:00"),

        _ => (null, $"'{spec.Key}' has no validator"),
    };

    /// <summary>
    /// Reads and validates a string.
    /// </summary>
    private static (JsonNode? Node, string? Error) ReadString(WritableSetting spec, JsonElement raw, Func<string, string?> validate)
    {
        if (raw.ValueKind != JsonValueKind.String)
            return (null, $"'{spec.Key}' expects text");

        var value = raw.GetString()!.Trim();
        return validate(value) is { } problem ? (null, problem) : (JsonValue.Create(value), null);
    }

    /// <summary>
    /// Reads a JSON array of strings, running every entry through the kind's own parser
    /// </summary>
    private static (JsonNode? Node, string? Error) ReadList(
        WritableSetting spec, JsonElement raw, Func<string, string?> validate)
    {
        if (raw.ValueKind != JsonValueKind.Array)
            return (null, $"'{spec.Key}' expects a list");

        var items = new JsonArray();
        foreach (var element in raw.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String)
                return (null, $"'{spec.Key}' expects a list of text entries");

            var value = (element.GetString() ?? "").Trim();
            if (value.Length == 0) continue;

            if (validate(value) is { } problem)
                return (null, problem);

            items.Add(JsonValue.Create(value));
        }

        if (spec.Max is { } max && items.Count > max)
            return (null, $"'{spec.Key}' allows at most {max:0} entries");

        return (items, null);
    }

    /// <summary>
    /// Reads a validated map.
    /// </summary>
    private static (JsonNode? Node, string? Error) ReadMap(
        WritableSetting spec, JsonElement raw, Regex keyFormat, Func<JsonElement, JsonNode?> read, string expected)
    {
        if (raw.ValueKind != JsonValueKind.Object)
            return (null, $"'{spec.Key}' expects a set of named values");

        var map = new JsonObject();
        foreach (var entry in raw.EnumerateObject())
        {
            if (!keyFormat.IsMatch(entry.Name))
                return (null, $"'{entry.Name}' is not a valid name for '{spec.Key}'");
            if (read(entry.Value) is not { } value)
                return (null, $"'{entry.Name}' in '{spec.Key}' must be {expected}");
            map[entry.Name] = value;
        }

        if (map.Count > 100)
            return (null, $"'{spec.Key}' allows at most 100 entries");

        return (map, null);
    }

    /// <summary>
    /// Link targets a page may render: http, https, mailto or a bare fragment
    /// </summary>
    public static bool IsSafeLink(string value) =>
        value == "#" ||
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto";

    private static bool IsEmail(string value) =>
        value.Length <= 254 && !value.Any(char.IsWhiteSpace) && System.Net.Mail.MailAddress.TryCreate(value, out var address) && address.Address == value;

    private static string? ParseIp(string value) =>
        IPAddress.TryParse(value, out _) ? null : $"'{value}' is not an IP address";

    private static string? ParseNetwork(string value)
    {
        if (!IPNetwork.TryParse(value, out var network))
            return $"'{value}' is not a network in CIDR form, such as 10.0.0.0/8";

        // refuses only a zero-length prefix, wide private ranges are the operator's call
        if (network.PrefixLength == 0)
            return $"'{value}' covers every address; name the proxy's network instead";

        return null;
    }

    private static string? ParseOrigin(string value)
    {
        if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return $"'{value}' must start with http:// or https://";

        var host = value.Split("//", 2)[1].TrimEnd('/');
        if (host.Length == 0 || host.Contains('/'))
            return $"'{value}' must be a scheme and host only, such as https://portway.example.com";

        // A bare wildcard would match every host the pattern's suffix allows nothing to narrow
        if (host is "*" || host.StartsWith("*.") && host.Count(c => c == '.') < 2)
            return $"'{value}' is too broad; a wildcard needs a registrable domain, such as https://*.example.com";

        return null;
    }

    private static string? ParseHost(string value) =>
        value == "*" || HostFormat.IsMatch(value) && value.Length <= 253 ||
        value.StartsWith('[') && value.EndsWith(']') && IPAddress.TryParse(value[1..^1], out _)
            ? null
            : $"'{value}' must be a host name, *.domain, an IP address or *";

    private static string? ParseExtension(string value) =>
        ExtensionFormat.IsMatch(value) ? null : $"'{value}' must be an extension such as .pdf";

    private static string? ParseMediaType(string value) =>
        MediaTypeHeaderValue.TryParse(value, out var media) && media.MediaType == value
            ? null
            : $"'{value}' must be a media type such as application/json";

    /// <summary>
    /// Relative path outside Portway folders.
    /// </summary>
    private static string? ParseRelativePath(string value)
    {
        if (value.Length > 200 || !RelativePathFormat.IsMatch(value))
            return $"'{value}' must be a relative path of letters, digits, dots, dashes and underscores, such as log/traffic";

        var segments = value.Split('/');
        if (segments.Any(s => s is "." or ".."))
            return $"'{value}' must stay inside the Portway folder";

        if (ReservedFolders.Contains(segments[0], StringComparer.OrdinalIgnoreCase))
            return $"'{value}' is inside a folder Portway manages";

        var name = segments[^1];
        if (ProtectedFiles.Contains(name, StringComparer.OrdinalIgnoreCase) || name.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase))
            return $"'{value}' names a file Portway manages";

        return null;
    }

    private static string? ParseRoute(string value)
    {
        if (value.Length > 64 || !RouteFormat.IsMatch(value))
            return $"'{value}' must be a lower-case path such as /metrics";

        return ReservedRoutes.Any(r => value == r || value.StartsWith(r + "/"))
            ? $"'{value}' is reserved by Portway"
            : null;
    }

    private static (JsonNode? Node, string? Error) ReadText(WritableSetting spec, JsonElement raw)
    {
        if (raw.ValueKind == JsonValueKind.Null) return (null, null);
        if (raw.ValueKind != JsonValueKind.String)
            return (null, $"'{spec.Key}' expects text");

        var value = raw.GetString() ?? "";
        if (spec.Max is { } max && value.Length > max)
            return (null, $"'{spec.Key}' must be {max:0} characters or fewer");
        return (JsonValue.Create(value), null);
    }

    private static (JsonNode? Node, string? Error) ReadInt(WritableSetting spec, JsonElement raw)
    {
        if (raw.ValueKind != JsonValueKind.Number || !raw.TryGetInt64(out var value))
            return (null, $"'{spec.Key}' expects a whole number");
        if (spec.Min is { } min && value < min)
            return (null, $"'{spec.Key}' must be {min:0} or more");
        if (spec.Max is { } max && value > max)
            return (null, $"'{spec.Key}' must be {max:0} or less");
        return (JsonValue.Create(value), null);
    }

    private static (JsonNode? Node, string? Error) ReadNumber(WritableSetting spec, JsonElement raw)
    {
        if (raw.ValueKind != JsonValueKind.Number || !raw.TryGetDouble(out var value))
            return (null, $"'{spec.Key}' expects a number");
        if (value < spec.Min || value > spec.Max)
            return (null, $"'{spec.Key}' must be between {spec.Min} and {spec.Max}");
        return (JsonValue.Create(value), null);
    }

    /// <summary>
    /// Rules across settings.
    /// </summary>
    private (string Message, string Field)? CrossCheck(IReadOnlyDictionary<string, JsonNode?> parsed)
    {
        string? Value(string key, string? fallback = null) =>
            parsed.TryGetValue(key, out var node) ? node?.ToString() : _root?[key] ?? fallback;
        long? Number(string key) => long.TryParse(Value(key), out var n) ? n : null;
        string? Touched(params string[] keys) => keys.FirstOrDefault(parsed.ContainsKey);

        if (Touched("SqlConnectionPooling:MinPoolSize", "SqlConnectionPooling:MaxPoolSize") is { } pool &&
            Number("SqlConnectionPooling:MinPoolSize") > Number("SqlConnectionPooling:MaxPoolSize"))
            return ("The minimum pool size cannot exceed the maximum", pool);

        if (Touched("Mcp:DefaultPageSize", "Mcp:MaxPageSize") is { } page &&
            Number("Mcp:DefaultPageSize") > Number("Mcp:MaxPageSize"))
            return ("The default MCP page size cannot exceed the maximum", page);

        if (Touched("Mcp:Path", "Telemetry:Prometheus:Path") is { } route &&
            string.Equals(Value("Mcp:Path", "/mcp"), Value("Telemetry:Prometheus:Path", "/metrics"), StringComparison.OrdinalIgnoreCase))
            return ("The MCP path and the metrics path must differ", route);

        // Uploads are served back
        if (Touched("FileStorage:StorageDirectory", "RequestTrafficLogging:LogDirectory", "RequestTrafficLogging:SqlitePath") is { } folder)
        {
            var storage = Value("FileStorage:StorageDirectory", "files")!;
            var logs = new[]
            {
                "log",
                Value("RequestTrafficLogging:LogDirectory", "log/traffic")!,
                Path.GetDirectoryName(Value("RequestTrafficLogging:SqlitePath", "log/traffic_logs.db"))!.Replace('\\', '/'),
            };
            if (logs.Any(log => Overlaps(storage, log)))
                return ("File storage cannot share a folder with log files", folder);
        }

        return null;
    }

    private static bool Overlaps(string a, string b)
    {
        a = a.Trim('/'); b = b.Trim('/');
        return a.Length > 0 && b.Length > 0 && (a.Equals(b, StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith(b + "/", StringComparison.OrdinalIgnoreCase) || b.StartsWith(a + "/", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Migrates deprecated and removed keys.
    /// </summary>
    internal static void MigrateDeprecatedKeys(JsonObject root)
    {
        foreach (var (current, deprecated) in ConfigAliases.RenamedKeys)
        {
            if (!TryTakePath(root, deprecated, out var value)) continue;
            if (!TryGetPath(root, current, out _)) SetPath(root, current, value);
        }

        foreach (var removed in ConfigAliases.RemovedKeys)
            TryTakePath(root, removed, out _);
    }

    private static async Task<JsonObject> ReadOverridesAsync()
    {
        if (!File.Exists(OverridesPath)) return new JsonObject();
        try
        {
            await using var stream = File.OpenRead(OverridesPath);
            return (await JsonNode.ParseAsync(stream)) as JsonObject ?? new JsonObject();
        }
        catch (JsonException ex)
        {
            Log.Warning(ex, "{File} is not valid JSON, starting a fresh overrides document", OverridesFileName);
            return new JsonObject();
        }
    }

    private static async Task WriteOverridesAsync(JsonObject root)
    {
        ConfigBackupService.Backup(OverridesPath);

        await AtomicFile.WriteAllTextAsync(OverridesPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static JsonObject? Parent(JsonObject root, string key, bool create)
    {
        var segments = key.Split(':');
        var node = root;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (node[segments[i]] is not JsonObject child)
            {
                if (!create) return null;
                child = new JsonObject();
                node[segments[i]] = child;
            }
            node = child;
        }
        return node;
    }

    private static string Leaf(string key) => key[(key.LastIndexOf(':') + 1)..];

    private static bool TryGetPath(JsonObject root, string key, out JsonNode? value)
    {
        value = null;
        return Parent(root, key, false) is { } parent && parent.TryGetPropertyValue(Leaf(key), out value);
    }

    private static bool TryTakePath(JsonObject root, string key, out JsonNode? value)
    {
        if (!TryGetPath(root, key, out value)) return false;
        Parent(root, key, false)!.Remove(Leaf(key));
        return true;
    }

    /// <summary>
    /// Writes a colon-separated configuration key into the nested JSON document
    /// </summary>
    private static void SetPath(JsonObject root, string key, JsonNode? value) =>
        Parent(root, key, true)![Leaf(key)] = value;
}
