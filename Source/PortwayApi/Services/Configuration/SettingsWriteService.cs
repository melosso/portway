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
    SettingKind Kind,
    bool RequiresRestart,
    double? Min = null,
    double? Max = null,
    string[]? Choices = null);

/// <summary>
/// Validator for a writable setting.
/// </summary>
public enum SettingKind
{
    Bool, Int, Number, Choice, Text, IpList, CidrList, OriginList, ExtList, MediaTypeList, IntMap, LevelMap, HostList, Clear, Secret, Name, AppName, Attributes, RelPath, DbFile, Route, PathBase, Link, WebUrl, Email, Time,
}

/// <summary>
/// Outcome of a write attempt; Field names the setting that failed so a caller can mark it
/// </summary>
public sealed record SettingsWriteResult(bool Ok, string? Error = null, string? Field = null, bool RestartRequired = false);

/// <summary>
/// Writes allowlisted settings to appsettings.overrides.json, never appsettings.json
/// </summary>
public sealed partial class SettingsWriteService
{
    public const string OverridesFileName = "appsettings.overrides.json";

    private static readonly string[] LogLevels =
        ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"];

    [GeneratedRegex(@"^([01]\d|2[0-3]):[0-5]\d$")]
    private static partial Regex ScheduleFormat();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,49}$")]
    private static partial Regex NameFormat();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_.]{0,99}$")]
    private static partial Regex NamespaceFormat();

    [GeneratedRegex(@"^[A-Za-z0-9._-]+(/[A-Za-z0-9._-]+)*$")]
    private static partial Regex RelativePathFormat();

    [GeneratedRegex(@"^(/[a-z0-9][a-z0-9_-]*)+$")]
    private static partial Regex RouteFormat();

    [GeneratedRegex(@"^(/[A-Za-z0-9._-]+)*$")]
    private static partial Regex PathBaseFormat();

    [GeneratedRegex(@"^(\*\.)?[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)*$")]
    private static partial Regex HostFormat();

    [GeneratedRegex(@"^\.[A-Za-z0-9]{1,10}$")]
    private static partial Regex ExtensionFormat();

    // Part of SQL connection strings
    [GeneratedRegex(@"^[A-Za-z0-9 ._()-]{1,128}$")]
    private static partial Regex ApplicationNameFormat();

    [GeneratedRegex(@"^([A-Za-z0-9._-]+=[^,=;\s]+(,[A-Za-z0-9._-]+=[^,=;\s]+)*)?$")]
    private static partial Regex ResourceAttributesFormat();

    private static readonly string[] ReservedFolders = ["endpoints", "environments", "tokens", "wwwroot", ".core", ".backups"];

    private static readonly string[] ProtectedFiles = ["auth.db", "mcp.db", "metrics.db"];

    private static readonly string[] ReservedRoutes = ["/api", "/docs", "/health", "/openapi"];

    private static readonly string[] ScalarThemes =
        ["portway", "default", "alternate", "moon", "purple", "solarized", "bluePlanet", "saturn", "kepler", "mars", "deepSpace", "elysiajs", "fastify", "laserwave", "none"];

    private static readonly string[] Providers = ["Memory", "Redis"];

    // Writable keys; anything else is refused by name
    private static readonly Dictionary<string, WritableSetting> Allowed = new WritableSetting[]
    {
        new("RateLimiting:Enabled", SettingKind.Bool, true),
        new("RateLimiting:IpLimit", SettingKind.Int, true, 1, 1_000_000),
        new("RateLimiting:IpWindowSeconds", SettingKind.Int, true, 1, 86_400),
        new("RateLimiting:TokenLimit", SettingKind.Int, true, 1, 1_000_000),
        new("RateLimiting:TokenWindowSeconds", SettingKind.Int, true, 1, 86_400),
        new("RateLimiting:Provider", SettingKind.Choice, true, Choices: Providers),
        new("RateLimiting:Redis:ConnectionString", SettingKind.Secret, true),

        new("Caching:Enabled", SettingKind.Bool, false),
        new("Caching:DefaultCacheDurationSeconds", SettingKind.Int, false, 0, 86_400),
        new("Caching:MemoryCacheSizeLimitMB", SettingKind.Int, true, 1, 8_192),
        new("Caching:Provider", SettingKind.Choice, true, Choices: Providers),
        new("Caching:EndpointCacheDurations", SettingKind.IntMap, false, 0, 86_400),
        new("Caching:CacheableContentTypes", SettingKind.MediaTypeList, false, Max: 50),
        new("Caching:Redis:ConnectionString", SettingKind.Secret, true),
        new("Caching:Redis:InstanceName", SettingKind.Text, true, Max: 100),
        new("Caching:Redis:Database", SettingKind.Int, true, 0, 255),
        new("Caching:Redis:UseSsl", SettingKind.Bool, true),
        new("Caching:Redis:ConnectTimeoutMs", SettingKind.Int, true, 100, 60_000),
        new("Caching:Redis:AbortOnConnectFail", SettingKind.Bool, true),
        new("Caching:Redis:FallbackToMemoryCache", SettingKind.Bool, true),
        new("Caching:Redis:MaxRetryAttempts", SettingKind.Int, true, 0, 10),
        new("Caching:Redis:RetryDelayMs", SettingKind.Int, true, 0, 60_000),

        new("SqlConnectionPooling:Enabled", SettingKind.Bool, true),
        new("SqlConnectionPooling:MinPoolSize", SettingKind.Int, true, 0, 1_000),
        new("SqlConnectionPooling:MaxPoolSize", SettingKind.Int, true, 1, 5_000),
        new("SqlConnectionPooling:ConnectionTimeoutSeconds", SettingKind.Int, true, 1, 600),
        new("SqlConnectionPooling:CommandTimeoutSeconds", SettingKind.Int, true, 1, 3_600),
        new("SqlConnectionPooling:ApplicationName", SettingKind.AppName, true),

        new("EndpointReloading:Enabled", SettingKind.Bool, false),
        new("EndpointReloading:DebounceMs", SettingKind.Int, false, 50, 60_000),

        new("Serilog:MinimumLevel:Default", SettingKind.Choice, true, Choices: LogLevels),
        new("Serilog:MinimumLevel:Override", SettingKind.LevelMap, true),

        new("RequestTrafficLogging:Enabled", SettingKind.Bool, true),
        new("RequestTrafficLogging:Provider", SettingKind.Choice, true, Choices: ["File", "Sqlite"]),
        new("RequestTrafficLogging:SqlitePath", SettingKind.DbFile, true),
        new("RequestTrafficLogging:LogDirectory", SettingKind.RelPath, true),
        new("RequestTrafficLogging:FilePrefix", SettingKind.Name, true),
        new("RequestTrafficLogging:MaxFileSizeMB", SettingKind.Int, true, 1, 1_024),
        new("RequestTrafficLogging:MaxFileCount", SettingKind.Int, true, 1, 100),
        new("RequestTrafficLogging:QueueCapacity", SettingKind.Int, true, 100, 1_000_000),
        new("RequestTrafficLogging:BatchSize", SettingKind.Int, true, 1, 10_000),
        new("RequestTrafficLogging:FlushIntervalMs", SettingKind.Int, true, 100, 60_000),
        new("RequestTrafficLogging:CaptureHeaders", SettingKind.Bool, true),
        new("RequestTrafficLogging:CaptureRequestBodies", SettingKind.Bool, true),
        new("RequestTrafficLogging:CaptureResponseBodies", SettingKind.Bool, true),
        new("RequestTrafficLogging:MaxBodyCaptureSizeBytes", SettingKind.Int, true, 0, 1_048_576),
        new("RequestTrafficLogging:InfoLoggingEnabled", SettingKind.Bool, true),

        new("DatabaseMaintenance:Enabled", SettingKind.Bool, false),
        new("DatabaseMaintenance:Schedule", SettingKind.Time, false),
        new("DatabaseMaintenance:RunOnStartup", SettingKind.Bool, true),
        new("DatabaseMaintenance:FreePageRatioThreshold", SettingKind.Number, false, 0.01, 0.99),
        new("DatabaseMaintenance:Backup:Enabled", SettingKind.Bool, false),
        new("DatabaseMaintenance:Backup:Retention", SettingKind.Int, false, 1, 100),

        new("Mcp:Enabled", SettingKind.Bool, true),
        new("Mcp:Path", SettingKind.Route, true),
        new("Mcp:ChatEnabled", SettingKind.Bool, true),
        new("Mcp:ToolTimeoutSeconds", SettingKind.Int, true, 1, 600),
        new("Mcp:DefaultPageSize", SettingKind.Int, true, 1, 1_000),
        new("Mcp:MaxPageSize", SettingKind.Int, true, 1, 1_000),
        new("Mcp:MaxToolResultChars", SettingKind.Int, true, 1_000, 1_000_000),

        new("Console:SecureCookies", SettingKind.Bool, true),
        new("Console:Customization:LandingPageEnabled", SettingKind.Bool, true),
        new("Console:Customization:PromoText", SettingKind.Text, false, Max: 2_000),
        new("Console:Customization:PromoLogin", SettingKind.Bool, false),
        new("Console:Customization:LoginFooter", SettingKind.Text, false, Max: 2_000),
        new("Console:CorsOrigins", SettingKind.OriginList, true, Max: 50),

        new("Oidc:Enabled", SettingKind.Bool, false),
        new("OpenApi:Enabled", SettingKind.Bool, true),
        new("OpenApi:Title", SettingKind.Text, false, Max: 200),
        new("OpenApi:Version", SettingKind.Name, false),
        new("OpenApi:Description", SettingKind.Text, false, Max: 4_000),
        new("OpenApi:Contact:Name", SettingKind.Text, false, Max: 200),
        new("OpenApi:Contact:Email", SettingKind.Email, false),
        new("OpenApi:ForceHttpsInProduction", SettingKind.Bool, false),
        new("OpenApi:ShowNamespaces", SettingKind.Bool, false),
        new("OpenApi:DefaultGroup", SettingKind.Name, false),
        new("OpenApi:ShowBadges", SettingKind.Bool, false),
        new("OpenApi:ServeMarkdown", SettingKind.Bool, false),
        new("OpenApi:ExternalDocs:Url", SettingKind.WebUrl, false),
        new("OpenApi:ExternalDocs:Description", SettingKind.Text, false, Max: 200),
        new("OpenApi:SecurityDefinition:Description", SettingKind.Text, true, Max: 500),
        new("OpenApi:Footer:Text", SettingKind.Text, false, Max: 200),
        new("OpenApi:Footer:Url", SettingKind.Link, false),
        new("OpenApi:Footer:Target", SettingKind.Choice, false, Choices: ["_blank", "_self"]),
        new("OpenApi:Footer:ShowSourceIcon", SettingKind.Bool, false),
        new("OpenApi:Scalar:Theme", SettingKind.Choice, false, Choices: ScalarThemes),
        new("OpenApi:Scalar:Layout", SettingKind.Choice, false, Choices: ["modern", "classic"]),
        new("OpenApi:Scalar:ShowSidebar", SettingKind.Bool, false),
        new("OpenApi:Scalar:HideDownloadButton", SettingKind.Bool, false),
        new("OpenApi:Scalar:HideModels", SettingKind.Bool, false),
        new("OpenApi:Scalar:HideClientButton", SettingKind.Bool, false),
        new("OpenApi:Scalar:HideTestRequestButton", SettingKind.Bool, false),

        new("Telemetry:Provider", SettingKind.Choice, true, Choices: ["None", "Otlp", "Prometheus"]),
        new("Telemetry:Otlp:Endpoint", SettingKind.WebUrl, true),
        new("Telemetry:ServiceName", SettingKind.Name, true),
        new("Telemetry:ResourceAttributes", SettingKind.Attributes, true, Max: 1_000),
        new("Telemetry:Prometheus:Path", SettingKind.Route, true),

        new("FileStorage:StorageDirectory", SettingKind.RelPath, true),
        new("FileStorage:MaxFileSizeBytes", SettingKind.Int, false, 1_024, 1_073_741_824),
        new("FileStorage:UseMemoryCache", SettingKind.Bool, false),
        new("FileStorage:MemoryCacheDurationSeconds", SettingKind.Int, false, 1, 86_400),
        new("FileStorage:MaxTotalMemoryCacheMB", SettingKind.Int, false, 1, 8_192),
        new("FileStorage:AllowedExtensions", SettingKind.ExtList, false, Max: 200),
        new("FileStorage:BlockedExtensions", SettingKind.ExtList, false, Max: 200),

        new("Proxy:Username", SettingKind.Text, true, Max: 200),
        new("Proxy:Password", SettingKind.Secret, true),
        new("Proxy:Domain", SettingKind.Text, true, Max: 200),

        // Deployment shape; the endpoint refuses changes that lock the caller out
        new("Console:PublicOrigins", SettingKind.OriginList, true, Max: 50),
        new("ForwardedHeaders:KnownProxies", SettingKind.IpList, true, Max: 50),
        new("ForwardedHeaders:KnownNetworks", SettingKind.CidrList, true, Max: 50),
        new("AllowedHosts", SettingKind.HostList, true, Max: 50),
        new("PathBase", SettingKind.PathBase, true),

        // Credentials: clear only, never set
        new("Console:AdminApiKey", SettingKind.Clear, true),
        new("Console:SeedPassword", SettingKind.Clear, true),
        new("Console:SeedToken", SettingKind.Clear, true),
        new("Console:Enabled", SettingKind.Bool, true),
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
    public static bool IsCollection(SettingKind kind) =>
        kind is SettingKind.OriginList or SettingKind.IpList or SettingKind.CidrList or SettingKind.ExtList or SettingKind.MediaTypeList or SettingKind.IntMap or SettingKind.LevelMap;

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
    public async Task<SettingsWriteResult> ApplyAsync(IDictionary<string, JsonElement> changes, CancellationToken cancellationToken = default)
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

            if (spec.Kind == SettingKind.Secret && raw.ValueKind == JsonValueKind.String && raw.GetString() is { Length: > 0 } && !SettingsEncryptionHelper.HasKeyPair)
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

        if (CrossCheck(parsed, _root) is { } crossError)
            return new SettingsWriteResult(false, crossError.Message, crossError.Field);

        await WriteLock.WaitAsync(cancellationToken);
        try
        {
            var root = await ReadOverridesAsync(cancellationToken);
            MigrateDeprecatedKeys(root);
            foreach (var (key, node) in parsed) SetPath(root, key, node);
            await WriteOverridesAsync(root);
            // file watchers miss in-place writes on a single-file bind mount
            _root?.Reload();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
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
        SettingKind.Bool => raw.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? (JsonValue.Create(raw.GetBoolean()), null)
            : (null, $"'{spec.Key}' expects true or false"),

        SettingKind.Int => ReadInt(spec, raw),

        SettingKind.Number => ReadNumber(spec, raw),

        SettingKind.Choice => raw.ValueKind == JsonValueKind.String
                    && spec.Choices!.Contains(raw.GetString(), StringComparer.OrdinalIgnoreCase)
            ? (JsonValue.Create(spec.Choices!.First(c => c.Equals(raw.GetString(), StringComparison.OrdinalIgnoreCase))), null)
            : (null, $"'{spec.Key}' must be one of {string.Join(", ", spec.Choices!)}"),

        // Free text bounded by Max; null clears the override back to whatever appsettings.json declares
        SettingKind.Text => ReadText(spec, raw),

        SettingKind.IpList => ReadList(spec, raw, ParseIp),
        SettingKind.CidrList => ReadList(spec, raw, ParseNetwork),
        SettingKind.OriginList => ReadList(spec, raw, ParseOrigin),
        SettingKind.ExtList => ReadList(spec, raw, ParseExtension),
        SettingKind.MediaTypeList => ReadList(spec, raw, ParseMediaType),

        SettingKind.IntMap => ReadMap(spec, raw, NameFormat(), value =>
            value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n) && n >= spec.Min && n <= spec.Max
                ? JsonValue.Create(n)
                : null, $"a whole number from {spec.Min:0} to {spec.Max:0}"),

        SettingKind.LevelMap => ReadMap(spec, raw, NamespaceFormat(), value =>
            value.ValueKind == JsonValueKind.String && LogLevels.FirstOrDefault(l => l.Equals(value.GetString(), StringComparison.OrdinalIgnoreCase)) is { } level
                ? JsonValue.Create(level)
                : null, $"one of {string.Join(", ", LogLevels)}"),

        // HostFiltering expects one string
        SettingKind.HostList => ReadList(spec, raw, ParseHost) switch
        {
            (JsonArray { Count: 0 }, null) => (null, $"'{spec.Key}' needs at least one host, or *"),
            (JsonArray hosts, null) => (JsonValue.Create(string.Join(';', hosts.Select(h => h!.GetValue<string>()))), null),
            var failed => failed
        },

        // Only ever accepts the empty string: a credential may be removed here, never introduced
        SettingKind.Clear => raw.ValueKind == JsonValueKind.String && raw.GetString() == ""
            ? (JsonValue.Create(""), null)
            : (null, $"'{spec.Key}' can only be cleared from here, not set"),

        // Encrypted; empty clears
        SettingKind.Secret => raw.ValueKind switch
        {
            JsonValueKind.Null => (null, null),
            JsonValueKind.String when raw.GetString() is "" => (null, null),
            JsonValueKind.String when raw.GetString() is { Length: <= 1_024 } secret && !secret.Any(char.IsControl)
                => (JsonValue.Create(SettingsEncryptionHelper.Encrypt(secret)), null),
            _ => (null, $"'{spec.Key}' must be text of 1024 characters or fewer")
        },

        // identifiers that land in routes, tag names and the docs page; empty restores the default
        SettingKind.Name => raw.ValueKind == JsonValueKind.String && (raw.GetString() is "" || NameFormat().IsMatch(raw.GetString()!))
            ? (JsonValue.Create(raw.GetString()), null)
            : (null, $"'{spec.Key}' allows letters, digits, dots, dashes and underscores only"),

        SettingKind.AppName => ReadString(spec, raw, value => ApplicationNameFormat().IsMatch(value)
            ? null
            : $"'{spec.Key}' allows letters, digits, spaces, dots, dashes, underscores and parentheses, up to 128 characters"),

        SettingKind.Attributes => ReadString(spec, raw, value => value.Length <= spec.Max && ResourceAttributesFormat().IsMatch(value)
            ? null
            : $"'{spec.Key}' must be key=value pairs separated by commas"),

        SettingKind.RelPath => ReadString(spec, raw, ParseRelativePath),

        SettingKind.DbFile => ReadString(spec, raw, value => ParseRelativePath(value) ??
            (value.EndsWith(".db", StringComparison.OrdinalIgnoreCase) ? null : $"'{value}' must end in .db")),

        SettingKind.Route => ReadString(spec, raw, ParseRoute),

        SettingKind.PathBase => ReadString(spec, raw, value =>
            value.Length <= 100 && PathBaseFormat().IsMatch(value) && !value.Split('/').Any(s => s is "." or "..")
                ? null
                : $"'{value}' must be empty or a path such as /portway"),

        SettingKind.Link => raw.ValueKind == JsonValueKind.String && IsSafeLink(raw.GetString()!)
            ? (JsonValue.Create(raw.GetString()), null)
            : (null, $"'{spec.Key}' must be an http, https or mailto link, or #"),

        SettingKind.WebUrl => raw.ValueKind == JsonValueKind.String && (raw.GetString() is "" || Uri.TryCreate(raw.GetString(), UriKind.Absolute, out var web) && web.Scheme is "http" or "https")
            ? (JsonValue.Create(raw.GetString()), null)
            : (null, $"'{spec.Key}' must be an http or https link, or empty"),

        SettingKind.Email => raw.ValueKind == JsonValueKind.String && (raw.GetString() is "" || IsEmail(raw.GetString()!))
            ? (JsonValue.Create(raw.GetString()), null)
            : (null, $"'{spec.Key}' must be an e-mail address"),

        SettingKind.Time => raw.ValueKind == JsonValueKind.String && ScheduleFormat().IsMatch(raw.GetString() ?? "")
            ? (JsonValue.Create(raw.GetString()), null)
            : (null, $"'{spec.Key}' must be a 24-hour time such as 03:00"),

        _ => throw new ArgumentOutOfRangeException(nameof(spec), spec.Kind, null),
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
        value == "*" || value.Length <= 253 && HostFormat().IsMatch(value) ||
        value.StartsWith('[') && value.EndsWith(']') && IPAddress.TryParse(value[1..^1], out _)
            ? null
            : $"'{value}' must be a host name, *.domain, an IP address or *";

    private static string? ParseExtension(string value) =>
        ExtensionFormat().IsMatch(value) ? null : $"'{value}' must be an extension such as .pdf";

    private static string? ParseMediaType(string value) =>
        MediaTypeHeaderValue.TryParse(value, out var media) && media.MediaType == value
            ? null
            : $"'{value}' must be a media type such as application/json";

    /// <summary>
    /// Relative path outside Portway folders.
    /// </summary>
    private static string? ParseRelativePath(string value)
    {
        if (value.Length > 200 || !RelativePathFormat().IsMatch(value))
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
        if (value.Length > 64 || !RouteFormat().IsMatch(value))
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
    internal static (string Message, string Field)? CrossCheck(IReadOnlyDictionary<string, JsonNode?> parsed, IConfiguration? config)
    {
        string? Value(string key, string? fallback = null) =>
            parsed.TryGetValue(key, out var node) ? node?.ToString() : config?[key] ?? fallback;
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

    private static async Task<JsonObject> ReadOverridesAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(OverridesPath)) return new JsonObject();
        try
        {
            await using var stream = File.OpenRead(OverridesPath);
            return (await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken)) as JsonObject ?? new JsonObject();
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
