namespace PortwayApi.Services.Configuration;

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Serilog;

/// <summary>A setting the Web UI and API are allowed to change, with the validation that guards it</summary>
public sealed record WritableSetting(
    string Key,
    string Kind,
    bool RequiresRestart,
    double? Min = null,
    double? Max = null,
    string[]? Choices = null);

/// <summary>Outcome of a write attempt; Field names the setting that failed so a caller can mark it</summary>
public sealed record SettingsWriteResult(bool Ok, string? Error = null, string? Field = null, bool RestartRequired = false);

/// <summary>
/// Applies a whitelisted subset of configuration through appsettings.overrides.json.
/// appsettings.json is never written: it stays the operator's file, and deleting the
/// overrides file restores whatever it declares.
/// </summary>
public sealed class SettingsWriteService
{
    public const string OverridesFileName = "appsettings.overrides.json";

    private static readonly string[] LogLevels =
        ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"];

    private static readonly Regex ScheduleFormat = new(@"^([01]\d|2[0-3]):[0-5]\d$", RegexOptions.Compiled);

    // Only these keys can be written. Anything absent is refused by name, including secrets,
    // connection strings, file system paths and anything that changes routing.
    private static readonly Dictionary<string, WritableSetting> Allowed =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["RateLimiting:Enabled"]                    = new("RateLimiting:Enabled", "bool", true),
            ["RateLimiting:IpLimit"]                    = new("RateLimiting:IpLimit", "int", true, 1, 1_000_000),
            ["RateLimiting:IpWindow"]                   = new("RateLimiting:IpWindow", "int", true, 1, 86_400),
            ["RateLimiting:TokenLimit"]                 = new("RateLimiting:TokenLimit", "int", true, 1, 1_000_000),
            ["RateLimiting:TokenWindow"]                = new("RateLimiting:TokenWindow", "int", true, 1, 86_400),

            ["Caching:Enabled"]                         = new("Caching:Enabled", "bool", false),
            ["Caching:DefaultCacheDurationSeconds"]     = new("Caching:DefaultCacheDurationSeconds", "int", false, 0, 86_400),
            ["Caching:MemoryCacheSizeLimitMB"]          = new("Caching:MemoryCacheSizeLimitMB", "int", false, 1, 8_192),

            ["SqlConnectionPooling:Enabled"]            = new("SqlConnectionPooling:Enabled", "bool", true),
            ["SqlConnectionPooling:MinPoolSize"]        = new("SqlConnectionPooling:MinPoolSize", "int", true, 0, 1_000),
            ["SqlConnectionPooling:MaxPoolSize"]        = new("SqlConnectionPooling:MaxPoolSize", "int", true, 1, 5_000),
            ["SqlConnectionPooling:ConnectionTimeout"]  = new("SqlConnectionPooling:ConnectionTimeout", "int", true, 1, 600),
            ["SqlConnectionPooling:CommandTimeout"]     = new("SqlConnectionPooling:CommandTimeout", "int", true, 1, 3_600),

            ["EndpointReloading:Enabled"]               = new("EndpointReloading:Enabled", "bool", false),
            ["EndpointReloading:DebounceMs"]            = new("EndpointReloading:DebounceMs", "int", false, 50, 60_000),

            ["Serilog:MinimumLevel:Default"]            = new("Serilog:MinimumLevel:Default", "choice", true, Choices: LogLevels),

            ["DatabaseMaintenance:Enabled"]             = new("DatabaseMaintenance:Enabled", "bool", true),
            ["DatabaseMaintenance:Schedule"]            = new("DatabaseMaintenance:Schedule", "time", true),

            ["Mcp:Enabled"]                             = new("Mcp:Enabled", "bool", true),
            ["Mcp:RequireAuthentication"]               = new("Mcp:RequireAuthentication", "bool", true),
            ["Mcp:AppsEnabled"]                         = new("Mcp:AppsEnabled", "bool", true),
            ["Mcp:ChatEnabled"]                         = new("Mcp:ChatEnabled", "bool", true),

            ["WebUi:SecureCookies"]                     = new("WebUi:SecureCookies", "bool", true),
        };

    private static readonly SemaphoreSlim WriteLock = new(1, 1);

    public static IReadOnlyCollection<WritableSetting> Schema => Allowed.Values;

    public static string OverridesPath => Path.Combine(Directory.GetCurrentDirectory(), OverridesFileName);

    /// <summary>
    /// Creates an empty overrides document when none exists. The configuration provider only
    /// watches a file that was present when it was added, so without this the first write
    /// would need a restart to take effect.
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

    /// <summary>Validates every entry, then writes them together; nothing is applied when one fails</summary>
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

        "time" => raw.ValueKind == JsonValueKind.String && ScheduleFormat.IsMatch(raw.GetString() ?? "")
            ? (JsonValue.Create(raw.GetString()), null)
            : (null, $"'{spec.Key}' must be a 24-hour time such as 03:00"),

        _ => (null, $"'{spec.Key}' has no validator"),
    };

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

    /// <summary>Writes a colon-separated configuration key into the nested JSON document</summary>
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
