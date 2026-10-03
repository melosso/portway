namespace PortwayApi.Services.Configuration;

using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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

    private static readonly string[] ScalarThemes =
        ["portway", "default", "alternate", "moon", "purple", "solarized", "bluePlanet", "saturn", "kepler", "mars", "deepSpace", "elysiajs", "fastify", "laserwave", "none"];

    // Writable keys; anything else is refused by name
    private static readonly Dictionary<string, WritableSetting> Allowed =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["RateLimiting:Enabled"] = new("RateLimiting:Enabled", "bool", true),
            ["RateLimiting:IpLimit"] = new("RateLimiting:IpLimit", "int", true, 1, 1_000_000),
            ["RateLimiting:IpWindow"] = new("RateLimiting:IpWindow", "int", true, 1, 86_400),
            ["RateLimiting:TokenLimit"] = new("RateLimiting:TokenLimit", "int", true, 1, 1_000_000),
            ["RateLimiting:TokenWindow"] = new("RateLimiting:TokenWindow", "int", true, 1, 86_400),

            ["Caching:Enabled"] = new("Caching:Enabled", "bool", false),
            ["Caching:DefaultCacheDurationSeconds"] = new("Caching:DefaultCacheDurationSeconds", "int", false, 0, 86_400),
            ["Caching:MemoryCacheSizeLimitMB"] = new("Caching:MemoryCacheSizeLimitMB", "int", false, 1, 8_192),

            ["SqlConnectionPooling:Enabled"] = new("SqlConnectionPooling:Enabled", "bool", true),
            ["SqlConnectionPooling:MinPoolSize"] = new("SqlConnectionPooling:MinPoolSize", "int", true, 0, 1_000),
            ["SqlConnectionPooling:MaxPoolSize"] = new("SqlConnectionPooling:MaxPoolSize", "int", true, 1, 5_000),
            ["SqlConnectionPooling:ConnectionTimeout"] = new("SqlConnectionPooling:ConnectionTimeout", "int", true, 1, 600),
            ["SqlConnectionPooling:CommandTimeout"] = new("SqlConnectionPooling:CommandTimeout", "int", true, 1, 3_600),

            ["EndpointReloading:Enabled"] = new("EndpointReloading:Enabled", "bool", false),
            ["EndpointReloading:DebounceMs"] = new("EndpointReloading:DebounceMs", "int", false, 50, 60_000),

            ["Serilog:MinimumLevel:Default"] = new("Serilog:MinimumLevel:Default", "choice", true, Choices: LogLevels),

            ["DatabaseMaintenance:Enabled"] = new("DatabaseMaintenance:Enabled", "bool", true),
            ["DatabaseMaintenance:Schedule"] = new("DatabaseMaintenance:Schedule", "time", true),

            ["Mcp:Enabled"] = new("Mcp:Enabled", "bool", true),
            ["Mcp:RequireAuthentication"] = new("Mcp:RequireAuthentication", "bool", true),
            ["Mcp:ChatEnabled"] = new("Mcp:ChatEnabled", "bool", true),

            ["WebUi:SecureCookies"] = new("WebUi:SecureCookies", "bool", true),
            ["WebUi:Customization:EnableLandingPage"] = new("WebUi:Customization:EnableLandingPage", "bool", true),
            ["WebUi:Customization:PromoText"] = new("WebUi:Customization:PromoText", "text", false, Max: 2_000),
            ["WebUi:Customization:PromoLogin"] = new("WebUi:Customization:PromoLogin", "bool", false),
            ["WebUi:Customization:LoginFooter"] = new("WebUi:Customization:LoginFooter", "text", false, Max: 2_000),

            ["Oidc:Enabled"] = new("Oidc:Enabled", "bool", false),
            ["OpenApi:Enabled"] = new("OpenApi:Enabled", "bool", true),
            ["OpenApi:Title"] = new("OpenApi:Title", "text", false, Max: 200),
            ["OpenApi:Version"] = new("OpenApi:Version", "name", false),
            ["OpenApi:Description"] = new("OpenApi:Description", "text", false, Max: 4_000),
            ["OpenApi:Contact:Name"] = new("OpenApi:Contact:Name", "text", false, Max: 200),
            ["OpenApi:Contact:Email"] = new("OpenApi:Contact:Email", "email", false),
            ["OpenApi:ForceHttpsInProduction"] = new("OpenApi:ForceHttpsInProduction", "bool", false),
            ["OpenApi:ShowNamespaces"] = new("OpenApi:ShowNamespaces", "bool", false),
            ["OpenApi:DefaultGroup"] = new("OpenApi:DefaultGroup", "name", false),
            ["OpenApi:ShowBadges"] = new("OpenApi:ShowBadges", "bool", false),
            ["OpenApi:MarkdownEnabled"] = new("OpenApi:MarkdownEnabled", "bool", false),
            ["OpenApi:ExternalDocs:Url"] = new("OpenApi:ExternalDocs:Url", "weburl", false),
            ["OpenApi:ExternalDocs:Description"] = new("OpenApi:ExternalDocs:Description", "text", false, Max: 200),
            ["OpenApi:SecurityDefinition:Description"] = new("OpenApi:SecurityDefinition:Description", "text", true, Max: 500),
            ["OpenApi:Footer:Text"] = new("OpenApi:Footer:Text", "text", false, Max: 200),
            ["OpenApi:Footer:Url"] = new("OpenApi:Footer:Url", "link", false),
            ["OpenApi:Footer:Target"] = new("OpenApi:Footer:Target", "choice", false, Choices: ["_blank", "_self"]),
            ["OpenApi:Footer:ShowSourceIcon"] = new("OpenApi:Footer:ShowSourceIcon", "bool", false),
            ["OpenApi:ScalarTheme"] = new("OpenApi:ScalarTheme", "choice", false, Choices: ScalarThemes),
            ["OpenApi:ScalarLayout"] = new("OpenApi:ScalarLayout", "choice", false, Choices: ["modern", "classic"]),
            ["OpenApi:ScalarShowSidebar"] = new("OpenApi:ScalarShowSidebar", "bool", false),
            ["OpenApi:ScalarHideDownloadButton"] = new("OpenApi:ScalarHideDownloadButton", "bool", false),
            ["OpenApi:ScalarHideModels"] = new("OpenApi:ScalarHideModels", "bool", false),
            ["OpenApi:ScalarHideClientButton"] = new("OpenApi:ScalarHideClientButton", "bool", false),
            ["OpenApi:ScalarHideTestRequestButton"] = new("OpenApi:ScalarHideTestRequestButton", "bool", false),
            ["RequestTrafficLogging:Enabled"] = new("RequestTrafficLogging:Enabled", "bool", true),

            ["FileStorage:MaxFileSizeBytes"] = new("FileStorage:MaxFileSizeBytes", "int", false, 1_024, 1_073_741_824),

            // Deployment shape; the endpoint refuses changes that lock the caller out
            ["WebUi:PublicOrigins"] = new("WebUi:PublicOrigins", "originlist", true, Max: 50),
            ["ForwardedHeaders:KnownProxies"] = new("ForwardedHeaders:KnownProxies", "iplist", true, Max: 50),
            ["ForwardedHeaders:KnownNetworks"] = new("ForwardedHeaders:KnownNetworks", "cidrlist", true, Max: 50),

            // Write-only in the safe direction: the seeding key can be cleared, never set
            ["WebUi:AdminApiKey"] = new("WebUi:AdminApiKey", "clear", true),
            ["WebUi:Enabled"] = new("WebUi:Enabled", "bool", true),
        };

    private static readonly SemaphoreSlim WriteLock = new(1, 1);

    public static IReadOnlyCollection<WritableSetting> Schema => Allowed.Values;

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

            var (node, error) = Coerce(spec, raw);
            if (error is not null)
                return new SettingsWriteResult(false, error, key);

            parsed[spec.Key] = node;
            restart |= spec.RequiresRestart;
        }

        if (CrossCheck(parsed) is { } crossError)
            return new SettingsWriteResult(false, crossError.Message, crossError.Field);

        await WriteLock.WaitAsync();
        try
        {
            var root = await ReadOverridesAsync();
            foreach (var (key, node) in parsed) SetPath(root, key, node);
            await WriteOverridesAsync(root);
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

    private static (JsonNode? Node, string? Error) Coerce(WritableSetting spec, JsonElement raw) => spec.Kind switch
    {
        "bool" => raw.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? (JsonValue.Create(raw.GetBoolean()), null)
            : (null, $"'{spec.Key}' expects true or false"),

        "int" => ReadInt(spec, raw),

        "choice" => raw.ValueKind == JsonValueKind.String
                    && spec.Choices!.Contains(raw.GetString(), StringComparer.OrdinalIgnoreCase)
            ? (JsonValue.Create(spec.Choices!.First(c => c.Equals(raw.GetString(), StringComparison.OrdinalIgnoreCase))), null)
            : (null, $"'{spec.Key}' must be one of {string.Join(", ", spec.Choices!)}"),

        // Free text bounded by Max; null clears the override back to whatever appsettings.json declares
        "text" => ReadText(spec, raw),

        "iplist" => ReadList(spec, raw, ParseIp),
        "cidrlist" => ReadList(spec, raw, ParseNetwork),
        "originlist" => ReadList(spec, raw, ParseOrigin),

        // Only ever accepts the empty string: a credential may be removed here, never introduced
        "clear" => raw.ValueKind == JsonValueKind.String && raw.GetString() == ""
            ? (JsonValue.Create(""), null)
            : (null, $"'{spec.Key}' can only be cleared from here, not set"),

        // identifiers that land in routes, tag names and the docs page; empty restores the default
        "name" => raw.ValueKind == JsonValueKind.String && (raw.GetString() is "" || NameFormat.IsMatch(raw.GetString()!))
            ? (JsonValue.Create(raw.GetString()), null)
            : (null, $"'{spec.Key}' allows letters, digits, dots, dashes and underscores only"),

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

    private static (string Message, string Field)? CrossCheck(IReadOnlyDictionary<string, JsonNode?> parsed)
    {
        if (parsed.TryGetValue("SqlConnectionPooling:MinPoolSize", out var minNode) &&
            parsed.TryGetValue("SqlConnectionPooling:MaxPoolSize", out var maxNode) &&
            minNode?.GetValue<long>() > maxNode?.GetValue<long>())
        {
            return ("The minimum pool size cannot exceed the maximum", "SqlConnectionPooling:MinPoolSize");
        }
        return null;
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

        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var temp = OverridesPath + ".tmp";
        await File.WriteAllTextAsync(temp, json);

        if (File.Exists(OverridesPath)) File.Replace(temp, OverridesPath, null);
        else File.Move(temp, OverridesPath);
    }

    /// <summary>
    /// Writes a colon-separated configuration key into the nested JSON document
    /// </summary>
    private static void SetPath(JsonObject root, string key, JsonNode? value)
    {
        var segments = key.Split(':');
        var node = root;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (node[segments[i]] is not JsonObject child)
            {
                child = new JsonObject();
                node[segments[i]] = child;
            }
            node = child;
        }
        node[segments[^1]] = value;
    }
}
