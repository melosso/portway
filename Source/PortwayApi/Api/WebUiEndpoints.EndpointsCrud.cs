namespace PortwayApi.Endpoints;

using PortwayApi.Auth;
using Serilog;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using PortwayApi.Classes;
using PortwayApi.Helpers;


public static partial class WebUiEndpointExtensions
{
    private static void MapEndpointCrudRoutes(WebApplication app, PortwayApi.Services.Configuration.ConfigAuditService configAudit)
    {
        void Audit(HttpContext ctx, string action, string targetType, string target, string? details = null, string? backupPath = null)
            => configAudit.Record(action, targetType, target, ctx.Connection.RemoteIpAddress?.ToString(), details, backupPath);

        app.MapMethods("/_/api/endpoints/{type}/{**name}", ["PATCH"], async (string type, string name, HttpContext context) =>
        {
            if (IsVersionPath(name))
                return Results.Json(new { error = "Rename the base endpoint; its versions move with it" }, statusCode: 400);

            var (filePath, err) = ResolveEndpointPath(type, name);
            if (err != null) return Results.Json(new { error = err }, statusCode: 400);
            if (!File.Exists(filePath!))
                return Results.Json(new { error = "Endpoint not found" }, statusCode: 404);

            var body = await context.Request.ReadFromJsonAsync<JsonElement>();
            var newName = body.TryGetProperty("new_name", out var nn) ? nn.GetString()?.Trim() ?? "" : "";
            if (!Regex.IsMatch(newName, @"^[a-zA-Z0-9_-]+(/[a-zA-Z0-9_-]+)*$"))
                return Results.Json(new { error = "Invalid name" }, statusCode: 400);

            var (newFilePath, newErr) = ResolveEndpointPath(type, newName);
            if (newErr != null) return Results.Json(new { error = newErr }, statusCode: 400);
            if (File.Exists(newFilePath!))
                return Results.Json(new { error = "An endpoint with that name already exists" }, statusCode: 409);

            var oldDir = Path.GetDirectoryName(filePath!)!;
            var oldFolderName = Path.GetFileName(oldDir);
            var newDir = Path.GetDirectoryName(newFilePath!)!;
            Directory.CreateDirectory(Path.GetDirectoryName(newDir)!);
            Directory.Move(oldDir, newDir);

            // A Namespace equal to the old folder name is cleared so the new key is newName
            var actualName = newName;
            try
            {
                var movedJson = await File.ReadAllTextAsync(newFilePath!);
                using var doc = JsonDocument.Parse(movedJson);
                if (doc.RootElement.TryGetProperty("Namespace", out var nsEl)
                    && nsEl.ValueKind == JsonValueKind.String
                    && string.Equals(nsEl.GetString(), oldFolderName, StringComparison.OrdinalIgnoreCase))
                {
                    var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(movedJson)!;
                    dict.Remove("Namespace");
                    await File.WriteAllTextAsync(newFilePath!,
                        JsonSerializer.Serialize(dict, SharedJsonOptions.Indented));
                }
            }
            catch (Exception ex) { Log.Warning(ex, "Renamed endpoint to {NewName} but could not clear doubled Namespace from entity.json", newName); }

            var epType = TypeStringToEndpointType(type);
            ReloadEndpoints(app.Services, epType);

            Audit(context, "rename", "endpoint", $"{type}/{name} -> {type}/{actualName}");
            return Results.Ok(new { ok = true, name = actualName });
        }).ExcludeFromDescription();

        // GET /_/api/endpoints/{type}/{**name}?raw=true , fetch a single endpoint file (structured or raw JSON)
        app.MapGet("/_/api/endpoints/{type}/{**name}", (string type, string name, HttpContext context) =>
        {
            var (filePath, err) = ResolveEndpointPath(type, name);
            if (err != null) return Results.Json(new { error = err }, statusCode: 400);

            if (!File.Exists(filePath!))
                return Results.Json(new { error = "Endpoint not found" }, statusCode: 404);

            try
            {
                var raw = File.ReadAllText(filePath!);
                if (context.Items[SignedInRoleKey] as string != AdminUserRoles.Administrator)
                    raw = RedactEndpointUrls(raw);
                var lastMod = new DateTimeOffset(File.GetLastWriteTimeUtc(filePath!), TimeSpan.Zero).ToUnixTimeSeconds();

                if (context.Request.Query["raw"] == "true")
                    return Results.Json(new { content = raw, last_modified = lastMod });

                var content = JsonDocument.Parse(raw).RootElement.Clone();
                return Results.Json(new { name, type, content, last_modified = lastMod, raw });
            }
            catch (Exception ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 500);
            }
        }).ExcludeFromDescription();

        // PUT /_/api/endpoints/{type}/{**name}?raw=true , overwrite a single endpoint file
        app.MapPut("/_/api/endpoints/{type}/{**name}", async (string type, string name, HttpContext context) =>
        {
            var (filePath, err) = ResolveEndpointPath(type, name);
            if (err != null) return Results.Json(new { error = err }, statusCode: 400);

            var body = await context.Request.ReadFromJsonAsync<JsonElement>();

            string rawContent;
            if (context.Request.Query["raw"] == "true")
            {
                if (!body.TryGetProperty("content", out var contentEl) || contentEl.ValueKind != JsonValueKind.String)
                    return Results.Json(new { error = "content field required" }, statusCode: 400);
                rawContent = contentEl.GetString() ?? "";
            }
            else
            {
                if (!body.TryGetProperty("content", out var contentEl) || contentEl.ValueKind == JsonValueKind.Undefined)
                    return Results.Json(new { error = "content field required" }, statusCode: 400);
                rawContent = contentEl.GetRawText();
            }

            var existing = File.Exists(filePath!) ? await File.ReadAllTextAsync(filePath!) : null;
            var format = FormatOf(existing);
            try
            {
                using var doc = JsonDocument.Parse(rawContent);
                rawContent = JsonSerializer.Serialize(doc.RootElement, format);
            }
            catch (JsonException ex) { return Results.Json(new { error = $"Invalid JSON: {ex.Message}" }, statusCode: 400); }

            rawContent = StampDeprecatedSince(rawContent, existing, format);

            if (ConfigError(type, rawContent) is { } putConfigError)
                return Results.Json(new { error = putConfigError }, statusCode: 400);

            Directory.CreateDirectory(Path.GetDirectoryName(filePath!)!);
            var backupPath = Services.Configuration.ConfigBackupService.Backup(filePath!);
            await File.WriteAllTextAsync(filePath!, existing?.EndsWith('\n') == true ? rawContent + "\n" : rawContent);
            var lastMod = new DateTimeOffset(File.GetLastWriteTimeUtc(filePath!), TimeSpan.Zero).ToUnixTimeSeconds();

            var epType = TypeStringToEndpointType(type);
            ReloadEndpoints(app.Services, epType);

            Audit(context, "update", "endpoint", $"{type}/{name}", null, backupPath);

            return Results.Ok(new { ok = true, last_modified = lastMod, warnings = await MissingSecretWarningsAsync(app.Services, rawContent) });
        }).ExcludeFromDescription();

        // POST /_/api/endpoints/{type} , create a new endpoint
        app.MapPost("/_/api/endpoints/{type}", async (string type, HttpContext context) =>
        {
            var body = await context.Request.ReadFromJsonAsync<JsonElement>();
            var name = body.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";

            if (!Regex.IsMatch(name, @"^[a-zA-Z0-9_-]+(/[a-zA-Z0-9_-]+)*$"))
                return Results.Json(new { error = "Invalid endpoint name. Use letters, numbers, hyphens, underscores, and forward slashes as separators." }, statusCode: 400);

            var (filePath, err) = ResolveEndpointPath(type, name);
            if (err != null) return Results.Json(new { error = err }, statusCode: 400);

            if (File.Exists(filePath!))
                return Results.Json(new { error = "Endpoint already exists" }, statusCode: 409);

            string rawContent;
            if (body.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String)
                rawContent = contentEl.GetString() ?? "{}";
            else if (body.TryGetProperty("content", out var contentObj) && contentObj.ValueKind == JsonValueKind.Object)
                rawContent = contentObj.GetRawText();
            else
                rawContent = "{}";

            try { using var _ = JsonDocument.Parse(rawContent); }
            catch (JsonException ex) { return Results.Json(new { error = $"Invalid JSON: {ex.Message}" }, statusCode: 400); }

            if (ConfigError(type, rawContent) is { } postConfigError)
                return Results.Json(new { error = postConfigError }, statusCode: 400);

            Directory.CreateDirectory(Path.GetDirectoryName(filePath!)!);
            await File.WriteAllTextAsync(filePath!, rawContent);

            var epType = TypeStringToEndpointType(type);
            ReloadEndpoints(app.Services, epType);

            Audit(context, "create", "endpoint", $"{type}/{name}");
            return Results.Json(new { ok = true, name, warnings = await MissingSecretWarningsAsync(app.Services, rawContent) }, statusCode: 201);
        }).ExcludeFromDescription();

        // DELETE /_/api/endpoints/{type}/{**name} , remove an endpoint
        app.MapDelete("/_/api/endpoints/{type}/{**name}", (string type, string name, HttpContext context) =>
        {
            var (filePath, err) = ResolveEndpointPath(type, name);
            if (err != null) return Results.Json(new { error = err }, statusCode: 400);

            if (!File.Exists(filePath!))
                return Results.Json(new { error = "Endpoint not found" }, statusCode: 404);

            if (!IsVersionPath(name) && VersionFolders(Path.GetDirectoryName(filePath!)!) is { Count: > 0 } versions)
                return Results.Json(new { error = $"Delete versions {string.Join(", ", versions)} first" }, statusCode: 409);

            var backupPath = Services.Configuration.ConfigBackupService.Backup(filePath!);
            var dir = Path.GetDirectoryName(filePath!);
            if (dir != null && Directory.Exists(dir))
                Directory.Delete(dir, true);
            else
                File.Delete(filePath!);

            var epType = TypeStringToEndpointType(type);
            ReloadEndpoints(app.Services, epType);

            Audit(context, "delete", "endpoint", $"{type}/{name}", null, backupPath);
            return Results.Ok(new { ok = true });
        }).ExcludeFromDescription();

        // copies the base entity.json and its sibling files into the next version folder
        app.MapPost("/_/api/endpoints/{type}/versions/{**name}", (string type, string name, HttpContext context) =>
        {
            var baseName = IsVersionPath(name) ? name[..name.LastIndexOf('/')] : name;
            var (filePath, err) = ResolveEndpointPath(type, name);
            if (err != null) return Results.Json(new { error = err }, statusCode: 400);
            if (!File.Exists(filePath!))
                return Results.Json(new { error = "Endpoint not found" }, statusCode: 404);

            var sourceDir = Path.GetDirectoryName(filePath!)!;
            var baseDir = IsVersionPath(name) ? Path.GetDirectoryName(sourceDir)! : sourceDir;
            var next = VersionFolders(baseDir).Select(EndpointVersion.Number).Append(1).Max() + 1;
            if (next > 999)
                return Results.Json(new { error = "Version limit reached" }, statusCode: 409);

            var targetDir = Path.Combine(baseDir, $"v{next}");
            Directory.CreateDirectory(targetDir);
            foreach (var source in Directory.GetFiles(sourceDir))
                File.Copy(source, Path.Combine(targetDir, Path.GetFileName(source)));

            var copied = Path.Combine(targetDir, "entity.json");
            File.WriteAllText(copied, WithoutLifecycle(File.ReadAllText(copied)));

            var epType = TypeStringToEndpointType(type);
            ReloadEndpoints(app.Services, epType);

            var versionName = $"{baseName}/v{next}";
            Audit(context, "create", "endpoint", $"{type}/{versionName}", $"new version of {name}");
            return Results.Ok(new { ok = true, name = versionName, version = $"v{next}" });
        }).ExcludeFromDescription();

        // POST /_/api/endpoints/{type}/validate , dry-run configuration check without saving
        app.MapPost("/_/api/endpoints/{type}/validate", async (string type, HttpContext context) =>
        {
            var body = await context.Request.ReadFromJsonAsync<JsonElement>();
            if (!body.TryGetProperty("content", out var contentEl) || contentEl.ValueKind == JsonValueKind.Undefined)
                return Results.Json(new { error = "content field required" }, statusCode: 400);

            var rawContent = contentEl.ValueKind == JsonValueKind.String
                ? contentEl.GetString() ?? ""
                : contentEl.GetRawText();

            JsonElement root;
            try
            {
                using var doc = JsonDocument.Parse(rawContent);
                root = doc.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                return Results.Json(new { valid = false, errors = new[] { $"Invalid JSON: {ex.Message}" } });
            }

            var errors = new List<string>();

            switch (type.ToLowerInvariant())
            {
                case "sql":
                case "webhook":
                    if (root.ValueKind != JsonValueKind.Object ||
                        !root.TryGetProperty("DatabaseObjectName", out var dbo) || dbo.ValueKind != JsonValueKind.String ||
                        string.IsNullOrWhiteSpace(dbo.GetString()))
                        errors.Add("DatabaseObjectName is required for SQL endpoints");
                    break;
                case "proxy":
                case "composite":
                    if (root.ValueKind != JsonValueKind.Object ||
                        !root.TryGetProperty("Url", out var url) || url.ValueKind != JsonValueKind.String ||
                        string.IsNullOrWhiteSpace(url.GetString()))
                        errors.Add("Url is required for Proxy endpoints");
                    if (root.ValueKind != JsonValueKind.Object ||
                        !root.TryGetProperty("Methods", out var methods) || methods.ValueKind != JsonValueKind.Array ||
                        methods.GetArrayLength() == 0)
                        errors.Add("Methods must contain at least one HTTP method");
                    break;
                case "file":
                case "static":
                    break;
                default:
                    return Results.Json(new { error = $"Unknown endpoint type: {type}" }, statusCode: 400);
            }

            if (TypeStringToEndpointType(type) is { } validateType)
                errors.AddRange(EndpointHandler.ValidateConfig(validateType, rawContent));

            // Same namespace rules the loader enforces at startup (charset, length, reserved names)
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("Namespace", out var ns) && ns.ValueKind == JsonValueKind.String)
            {
                errors.AddRange(DirectoryHelper.ValidateNamespaceName(ns.GetString() ?? ""));
            }

            return Results.Json(new { valid = errors.Count == 0, errors });
        }).ExcludeFromDescription();

        // Client JS errors, unauthenticated for sendBeacon and IP rate limited
    }

    /// <summary>
    /// One warning per referenced secret without a value in an environment the endpoint allows
    /// </summary>
    internal static async Task<List<string>> MissingSecretWarningsAsync(IServiceProvider services, string json)
    {
        var names = SecretReference.Names(json);
        if (names.Count == 0)
            return [];

        List<string> environments;
        using (var doc = JsonDocument.Parse(json))
        {
            environments = doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("AllowedEnvironments", out var allowed) && allowed.ValueKind == JsonValueKind.Array && allowed.GetArrayLength() > 0
                ? [.. allowed.EnumerateArray().Select(e => e.GetString()).OfType<string>()]
                : services.GetRequiredService<EnvironmentSettings>().GetAllowedEnvironments();
        }

        var provider = services.GetRequiredService<PortwayApi.Interfaces.IEnvironmentSettingsProvider>();
        var warnings = new List<string>();
        foreach (var env in environments)
        {
            foreach (var name in names)
            {
                try
                {
                    if (await provider.GetSecretAsync(env, name) is null)
                        warnings.Add($"Secret {name} has no value in environment {env}");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    warnings.Add($"Environment {env} could not be read to check secret {name}");
                }
            }
        }
        return warnings;
    }

    private static string? ConfigError(string type, string json) =>
        TypeStringToEndpointType(type) is { } endpointType && EndpointHandler.ValidateConfig(endpointType, json) is { Count: > 0 } errors
            ? string.Join("; ", errors)
            : null;

    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly JsonSerializerOptions IndentedJson4 = new(IndentedJson) { IndentSize = 4 };

    private static JsonSerializerOptions FormatOf(string? existing) =>
        existing is not null && FourSpaceIndent().IsMatch(existing) ? IndentedJson4 : IndentedJson;

    [GeneratedRegex(@"\A\s*\{[ \t]*\r?\n {4}\S")]
    private static partial Regex FourSpaceIndent();

    internal static string RedactEndpointUrls(string json)
    {
        if (JsonNode.Parse(json) is not JsonObject entity)
            return json;

        foreach (var (key, value) in entity.ToList())
        {
            if (key.Equals("Url", StringComparison.OrdinalIgnoreCase) && value is JsonValue url && url.TryGetValue<string>(out var text))
                entity[key] = UrlRedaction.Redact(text);
            else if (key.Equals("FallbackUrls", StringComparison.OrdinalIgnoreCase) && value is JsonArray urls)
                entity[key] = new JsonArray([.. urls.Select(u => (JsonNode?)UrlRedaction.Redact(u?.GetValue<string>()))]);
            // a reference names a secret, a literal is the credential itself
            else if (key.Equals("Headers", StringComparison.OrdinalIgnoreCase) && value is JsonObject headers)
                foreach (var (name, header) in headers.ToList())
                    headers[name] = header is JsonValue v && v.TryGetValue<string>(out var headerValue) && SecretReference.ExactName(headerValue) is not null ? headerValue : "Set";
        }

        return entity.ToJsonString();
    }

    private static readonly string[] LifecycleKeys = ["Deprecated", "DeprecatedSince", "Sunset", "Stability"];

    // the deprecation header needs a date, an endpoint deprecated by this save gets today
    private static string StampDeprecatedSince(string json, string? existing, JsonSerializerOptions format)
    {
        if (JsonNode.Parse(json) is not JsonObject entity
            || !IsDeprecated(entity)
            || entity.Any(p => p.Key.Equals("DeprecatedSince", StringComparison.OrdinalIgnoreCase))
            || (existing is not null && TryParseObject(existing) is { } previous && IsDeprecated(previous)))
            return json;

        entity["DeprecatedSince"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'00:00:00'Z'", System.Globalization.CultureInfo.InvariantCulture);
        return entity.ToJsonString(format);
    }

    private static bool IsDeprecated(JsonObject entity) =>
        entity.Any(p => p.Key.Equals("Deprecated", StringComparison.OrdinalIgnoreCase) && p.Value?.GetValueKind() == JsonValueKind.True);

    private static JsonObject? TryParseObject(string json)
    {
        try { return JsonNode.Parse(json) as JsonObject; }
        catch (JsonException) { return null; }
    }

    // a new version starts without the base endpoint's deprecation
    private static string WithoutLifecycle(string json)
    {
        if (JsonNode.Parse(json) is not JsonObject entity)
            return json;

        foreach (var key in entity.Select(p => p.Key).Where(k => LifecycleKeys.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList())
            entity.Remove(key);
        return entity.ToJsonString(IndentedJson);
    }

    private static bool IsVersionPath(string name) =>
        name.Contains('/') && EndpointVersion.TryParse(name[(name.LastIndexOf('/') + 1)..], out _);

    private static List<string> VersionFolders(string endpointDir) =>
        Directory.Exists(endpointDir)
            ? Directory.GetDirectories(endpointDir)
                .Select(Path.GetFileName)
                .Where(d => EndpointVersion.TryParse(d, out _) && File.Exists(Path.Combine(endpointDir, d!, "entity.json")))
                .Select(d => d!)
                .OrderBy(EndpointVersion.Number)
                .ToList()
            : [];
}
