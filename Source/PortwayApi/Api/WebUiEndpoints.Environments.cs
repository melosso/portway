namespace PortwayApi.Endpoints;

using PortwayApi.Auth;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using PortwayApi.Classes;
using PortwayApi.Helpers;
using PortwayApi.Interfaces;


public static partial class WebUiEndpointExtensions
{
    private static readonly SemaphoreSlim EnvironmentListLock = new(1, 1);

    // environment list edits read AllowedEnvironments and rewrite environments/settings.json, so two at once lose one
    private static async ValueTask<object?> SerializeEnvironmentListEdit(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        await EnvironmentListLock.WaitAsync(context.HttpContext.RequestAborted);
        try
        {
            return await next(context);
        }
        finally
        {
            EnvironmentListLock.Release();
        }
    }

    private static void MapEnvironmentRoutes(WebApplication app, PortwayApi.Services.Configuration.ConfigAuditService configAudit)
    {
        void Audit(HttpContext ctx, string action, string targetType, string target, string? details = null, string? backupPath = null)
            => configAudit.Record(action, targetType, target, ctx.Connection.RemoteIpAddress?.ToString(), details, backupPath);

        app.MapGet("/_/api/environments", () =>
        {
            var envSettings = app.Services.GetRequiredService<EnvironmentSettings>();
            var globalPath = Path.Combine(Directory.GetCurrentDirectory(), "environments", "settings.json");
            var lastModified = File.Exists(globalPath)
                ? new DateTimeOffset(File.GetLastWriteTimeUtc(globalPath), TimeSpan.Zero).ToUnixTimeSeconds()
                : 0L;
            return Results.Json(new
            {
                server_name = envSettings.ServerName,
                allowed_environments = envSettings.AllowedEnvironments,
                last_modified = lastModified
            });
        }).ExcludeFromDescription();

        // Environment CRUD
        app.MapPut("/_/api/environments/settings", async (HttpContext context) =>
        {
            var body = await context.Request.ReadFromJsonAsync<JsonElement>();
            var envSettings = app.Services.GetRequiredService<EnvironmentSettings>();
            var globalPath = Path.Combine(Directory.GetCurrentDirectory(), "environments", "settings.json");

            var serverName = body.TryGetProperty("server_name", out var sn) && sn.ValueKind == JsonValueKind.String
                ? sn.GetString() ?? envSettings.ServerName : envSettings.ServerName;
            var allowedEnvs = body.TryGetProperty("allowed_environments", out var ae) && ae.ValueKind == JsonValueKind.Array
                ? ae.EnumerateArray().Select(e => e.GetString() ?? "").Where(e => !string.IsNullOrEmpty(e)).ToList()
                : envSettings.AllowedEnvironments;

            var backupPath = Services.Configuration.ConfigBackupService.Backup(globalPath);
            var model = new { Environment = new { ServerName = serverName, AllowedEnvironments = allowedEnvs } };
            await AtomicFile.WriteAllTextAsync(globalPath, JsonSerializer.Serialize(model, SharedJsonOptions.Indented));
            envSettings.Reload();
            Audit(context, "update", "environment-settings", "environments/settings.json", null, backupPath);
            return Results.Ok(new { ok = true });
        }).AddEndpointFilter(SerializeEnvironmentListEdit).ExcludeFromDescription();

        app.MapGet("/_/api/environments/{name}", (string name, HttpContext context) =>
        {
            var isAdministrator = context.Items[SignedInRoleKey] as string == AdminUserRoles.Administrator;
            if (!EnvironmentNamePattern().IsMatch(name))
                return Results.Json(new { error = "Invalid environment name" }, statusCode: 400);

            var envPath = Path.Combine(Directory.GetCurrentDirectory(), "environments", name, "settings.json");
            if (!File.Exists(envPath))
                return Results.Json(new
                {
                    name,
                    exists = false,
                    server_name = (string?)null,
                    connection_string = (string?)null,
                    connection_string_encrypted = false,
                    headers = new Dictionary<string, string>(),
                    last_modified = 0L
                });

            try
            {
                var json = File.ReadAllText(envPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var cs = root.TryGetProperty("ConnectionString", out var csEl) ? csEl.GetString() : null;
                var sn = root.TryGetProperty("ServerName", out var snEl) ? snEl.GetString() : null;
                var hdrs = new Dictionary<string, string>();
                if (root.TryGetProperty("Headers", out var hEl) && hEl.ValueKind == JsonValueKind.Object)
                    foreach (var h in hEl.EnumerateObject())
                        hdrs[h.Name] = h.Value.GetString() ?? "";
                var lastMod = new DateTimeOffset(File.GetLastWriteTimeUtc(envPath), TimeSpan.Zero).ToUnixTimeSeconds();
                var isEncrypted = cs != null && cs.StartsWith("PWENC:");
                var secretNames = root.TryGetProperty("Secrets", out var secretsEl) && secretsEl.ValueKind == JsonValueKind.Object
                    ? secretsEl.EnumerateObject().Select(p => p.Name).Order(StringComparer.OrdinalIgnoreCase).ToList()
                    : [];

                return Results.Json(new
                {
                    name,
                    exists = true,
                    secret_names = secretNames,
                    server_name = sn,
                    connection_string = isEncrypted || !isAdministrator ? null : cs,
                    connection_string_encrypted = isEncrypted,
                    headers = isAdministrator ? hdrs : hdrs.ToDictionary(h => h.Key, _ => "Set"),
                    last_modified = lastMod
                });
            }
            catch (Exception ex)
            {
                return Results.Json(new { error = $"Failed to read settings: {ex.Message}" }, statusCode: 500);
            }
        }).ExcludeFromDescription();

        app.MapGet("/_/api/environments/{name}/raw", (string name, HttpContext context) =>
        {
            if (context.Items[SignedInRoleKey] as string != AdminUserRoles.Administrator)
                return Results.Json(new { error = "This action needs an administrator account" }, statusCode: 403);

            if (!EnvironmentNamePattern().IsMatch(name))
                return Results.Json(new { error = "Invalid environment name" }, statusCode: 400);

            var envPath = Path.Combine(Directory.GetCurrentDirectory(), "environments", name, "settings.json");
            if (!File.Exists(envPath))
                return Results.Json(new { error = "File not found" }, statusCode: 404);

            try
            {
                var raw = File.ReadAllText(envPath);
                var lastMod = new DateTimeOffset(File.GetLastWriteTimeUtc(envPath), TimeSpan.Zero).ToUnixTimeSeconds();
                return Results.Json(new { content = raw, last_modified = lastMod });
            }
            catch (Exception ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 500);
            }
        }).ExcludeFromDescription();

        app.MapPut("/_/api/environments/{name}/raw", async (string name, HttpContext context) =>
        {
            if (!EnvironmentNamePattern().IsMatch(name))
                return Results.Json(new { error = "Invalid environment name" }, statusCode: 400);

            var body = await context.Request.ReadFromJsonAsync<JsonElement>();
            if (!body.TryGetProperty("content", out var contentEl) || contentEl.ValueKind != JsonValueKind.String)
                return Results.Json(new { error = "content field required" }, statusCode: 400);

            var raw = contentEl.GetString() ?? "";
            JsonElement root;
            try
            {
                using var testDoc = JsonDocument.Parse(raw);
                root = testDoc.RootElement.Clone();
                if (!root.TryGetProperty("ConnectionString", out _) &&
                    !root.TryGetProperty("ServerName", out _) &&
                    !root.TryGetProperty("Headers", out _))
                    return Results.Json(new { error = "JSON must contain at least one of: ConnectionString, ServerName, Headers" }, statusCode: 400);
            }
            catch (JsonException ex)
            {
                return Results.Json(new { error = $"Invalid JSON: {ex.Message}" }, statusCode: 400);
            }

            // raw saves never bypass encryption, unless the file opts out
            if (System.Text.Json.Nodes.JsonNode.Parse(raw) is System.Text.Json.Nodes.JsonObject rawObject && EncryptAtRest(rawObject))
                raw = rawObject.ToJsonString(SharedJsonOptions.Indented);

            var envPath = Path.Combine(Directory.GetCurrentDirectory(), "environments", name, "settings.json");
            Directory.CreateDirectory(Path.GetDirectoryName(envPath)!);
            var backupPath = Services.Configuration.ConfigBackupService.Backup(envPath);
            await AtomicFile.WriteAllTextAsync(envPath, raw);
            Audit(context, "update-raw", "environment", name, null, backupPath);
            var lastMod = new DateTimeOffset(File.GetLastWriteTimeUtc(envPath), TimeSpan.Zero).ToUnixTimeSeconds();
            return Results.Ok(new { ok = true, last_modified = lastMod });
        }).ExcludeFromDescription();

        // write-only: no route returns a secret value
        app.MapPut("/_/api/environments/{name}/secrets/{secret}", async (string name, string secret, HttpContext context) =>
        {
            if (!EnvironmentNamePattern().IsMatch(name) || !SecretReference.IsValidName(secret))
                return Results.Json(new { error = "Invalid environment or secret name" }, statusCode: 400);

            var body = await context.Request.ReadFromJsonAsync<JsonElement>();
            if (!body.TryGetProperty("value", out var valueEl) || valueEl.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(valueEl.GetString()))
                return Results.Json(new { error = "A secret value is required" }, statusCode: 400);

            return await EditSecretsAsync(context, name, secret, secrets =>
            {
                RemoveSecret(secrets, secret);
                secrets[secret] = valueEl.GetString();
                return true;
            }, "set-secret");
        }).ExcludeFromDescription();

        app.MapDelete("/_/api/environments/{name}/secrets/{secret}", async (string name, string secret, HttpContext context) =>
        {
            if (!EnvironmentNamePattern().IsMatch(name) || !SecretReference.IsValidName(secret))
                return Results.Json(new { error = "Invalid environment or secret name" }, statusCode: 400);

            return await EditSecretsAsync(context, name, secret, secrets => RemoveSecret(secrets, secret), "delete-secret");
        }).ExcludeFromDescription();

        async Task<IResult> EditSecretsAsync(HttpContext context, string name, string secret, Func<System.Text.Json.Nodes.JsonObject, bool> edit, string action)
        {
            var envPath = Path.Combine(Directory.GetCurrentDirectory(), "environments", name, "settings.json");
            if (!File.Exists(envPath))
                return Results.Json(new { error = "Environment not found" }, statusCode: 404);

            if (System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(envPath)) is not System.Text.Json.Nodes.JsonObject settings)
                return Results.Json(new { error = "settings.json is not a JSON object" }, statusCode: 400);

            var secrets = settings["Secrets"] as System.Text.Json.Nodes.JsonObject ?? new System.Text.Json.Nodes.JsonObject();
            settings["Secrets"] = secrets;
            if (!edit(secrets))
                return Results.Json(new { error = "Secret not found" }, statusCode: 404);

            EncryptAtRest(settings);
            var backupPath = Services.Configuration.ConfigBackupService.Backup(envPath);
            await AtomicFile.WriteAllTextAsync(envPath, settings.ToJsonString(SharedJsonOptions.Indented));
            Audit(context, action, "environment-secret", $"{name}/{secret}", null, backupPath);
            var lastMod = new DateTimeOffset(File.GetLastWriteTimeUtc(envPath), TimeSpan.Zero).ToUnixTimeSeconds();
            return Results.Ok(new { ok = true, last_modified = lastMod });
        }

        app.MapPost("/_/api/environments/{name}/test", async (string name) =>
        {
            if (!EnvironmentNamePattern().IsMatch(name))
                return Results.Json(new { ok = false, error = "Invalid environment name" }, statusCode: 400);

            var envPath = Path.Combine(Directory.GetCurrentDirectory(), "environments", name, "settings.json");
            if (!File.Exists(envPath))
                return Results.Json(new { ok = false, error = "No settings.json found for this environment" }, statusCode: 404);

            try
            {
                var provider = app.Services.GetRequiredService<IEnvironmentSettingsProvider>();
                var (connectionString, _, _) = await provider.LoadEnvironmentOrThrowAsync(name);

                var sqlProviderFactory = app.Services.GetRequiredService<PortwayApi.Services.Providers.ISqlProviderFactory>();
                var sqlProvider = sqlProviderFactory.GetProvider(connectionString);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await using var conn = sqlProvider.CreateConnection(connectionString);
                await conn.OpenAsync(cts.Token);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = sqlProvider.HealthCheckQuery;
                cmd.CommandTimeout = 4;
                await cmd.ExecuteScalarAsync(cts.Token);

                return Results.Ok(new { ok = true, message = "Connection successful" });
            }
            catch (Exception ex)
            {
                var msg = ex.InnerException?.Message ?? ex.Message;
                return Results.Json(new { ok = false, error = msg });
            }
        }).ExcludeFromDescription();

        app.MapPost("/_/api/environments", async (HttpContext context) =>
        {
            var body = await context.Request.ReadFromJsonAsync<JsonElement>();
            var name = body.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var envSettings = app.Services.GetRequiredService<EnvironmentSettings>();
            var globalPath = Path.Combine(Directory.GetCurrentDirectory(), "environments", "settings.json");

            if (!EnvironmentNamePattern().IsMatch(name))
                return Results.Json(new { error = "Invalid environment name. Use only letters, numbers, hyphens, and underscores." }, statusCode: 400);
            if (envSettings.AllowedEnvironments.Contains(name, StringComparer.OrdinalIgnoreCase))
                return Results.Json(new { error = "Environment already exists" }, statusCode: 409);

            var envDir = Path.Combine(Directory.GetCurrentDirectory(), "environments", name);
            var envSettingsPath = Path.Combine(envDir, "settings.json");
            Directory.CreateDirectory(envDir);

            var serverName = body.TryGetProperty("server_name", out var sn) && sn.ValueKind == JsonValueKind.String
                ? sn.GetString() ?? envSettings.ServerName : envSettings.ServerName;
            var connStr = body.TryGetProperty("connection_string", out var cs) && cs.ValueKind == JsonValueKind.String
                ? cs.GetString() ?? "" : "";
            var headers = new Dictionary<string, string>();
            if (body.TryGetProperty("headers", out var hdrs) && hdrs.ValueKind == JsonValueKind.Object)
                foreach (var h in hdrs.EnumerateObject())
                    headers[h.Name] = h.Value.GetString() ?? "";

            var encryptedCs = !string.IsNullOrEmpty(connStr)
                ? SettingsEncryptionHelper.Encrypt(connStr) : "";
            var envModel = new { ConnectionString = encryptedCs, ServerName = serverName, Headers = headers };
            await AtomicFile.WriteAllTextAsync(envSettingsPath,
                JsonSerializer.Serialize(envModel, SharedJsonOptions.Indented));

            var newAllowed = envSettings.AllowedEnvironments;
            newAllowed.Add(name);
            var globalModel = new { Environment = new { ServerName = envSettings.ServerName, AllowedEnvironments = newAllowed } };
            await AtomicFile.WriteAllTextAsync(globalPath,
                JsonSerializer.Serialize(globalModel, SharedJsonOptions.Indented));
            envSettings.Reload();
            Audit(context, "create", "environment", name);
            return Results.Json(new { ok = true, name }, statusCode: 201);
        }).AddEndpointFilter(SerializeEnvironmentListEdit).ExcludeFromDescription();

        app.MapPut("/_/api/environments/{name}", async (string name, HttpContext context) =>
        {
            if (!EnvironmentNamePattern().IsMatch(name))
                return Results.Json(new { error = "Invalid environment name" }, statusCode: 400);

            var envDir = Path.Combine(Directory.GetCurrentDirectory(), "environments", name);
            var envSettingsPath = Path.Combine(envDir, "settings.json");
            var existingJson = File.Exists(envSettingsPath) ? File.ReadAllText(envSettingsPath) : "{}";
            JsonElement existing;
            try { existing = JsonDocument.Parse(existingJson).RootElement.Clone(); }
            catch { existing = JsonDocument.Parse("{}").RootElement.Clone(); }

            var body = await context.Request.ReadFromJsonAsync<JsonElement>();
            var serverName = body.TryGetProperty("server_name", out var sn) && sn.ValueKind == JsonValueKind.String
                ? sn.GetString()
                : (existing.TryGetProperty("ServerName", out var esn) ? esn.GetString() : null);

            // Honor the file's Encrypt opt-out so UI saves keep dev environments plaintext
            bool encryptOptOut = existing.TryGetProperty("Encrypt", out var encEl) && encEl.ValueKind == JsonValueKind.False;

            var existingCs = existing.TryGetProperty("ConnectionString", out var ecs) ? ecs.GetString() ?? "" : "";
            string newCs;
            if (body.TryGetProperty("connection_string", out var cs) && cs.ValueKind == JsonValueKind.String &&
                !string.IsNullOrEmpty(cs.GetString()))
                newCs = encryptOptOut ? cs.GetString()! : SettingsEncryptionHelper.Encrypt(cs.GetString()!);
            else
                newCs = existingCs;

            var headers = new Dictionary<string, string>();
            if (body.TryGetProperty("headers", out var hdrs) && hdrs.ValueKind == JsonValueKind.Object)
                foreach (var h in hdrs.EnumerateObject())
                    headers[h.Name] = h.Value.GetString() ?? "";
            else if (existing.TryGetProperty("Headers", out var ehdrs) && ehdrs.ValueKind == JsonValueKind.Object)
                foreach (var h in ehdrs.EnumerateObject())
                    headers[h.Name] = h.Value.GetString() ?? "";

            Directory.CreateDirectory(envDir);
            var backupPath = Services.Configuration.ConfigBackupService.Backup(envSettingsPath);

            // keep every field the edit form does not manage, Secrets included
            var envModel = new Dictionary<string, object?>();
            if (existing.ValueKind == JsonValueKind.Object)
                foreach (var property in existing.EnumerateObject())
                    envModel[property.Name] = JsonSerializer.Deserialize<object>(property.Value.GetRawText());
            envModel["ConnectionString"] = newCs;
            envModel["ServerName"] = serverName;
            envModel["Headers"] = headers;

            await AtomicFile.WriteAllTextAsync(envSettingsPath,
                JsonSerializer.Serialize(envModel, SharedJsonOptions.Indented));
            Audit(context, "update", "environment", name, null, backupPath);
            var lastMod = new DateTimeOffset(File.GetLastWriteTimeUtc(envSettingsPath), TimeSpan.Zero).ToUnixTimeSeconds();
            return Results.Ok(new { ok = true, last_modified = lastMod });
        }).ExcludeFromDescription();

        app.MapMethods("/_/api/environments/{name}", ["PATCH"], async (string name, HttpContext context) =>
        {
            if (!EnvironmentNamePattern().IsMatch(name))
                return Results.Json(new { error = "Invalid environment name" }, statusCode: 400);

            var body = await context.Request.ReadFromJsonAsync<JsonElement>();
            var newName = body.TryGetProperty("new_name", out var nn) ? nn.GetString()?.Trim() ?? "" : "";
            if (!EnvironmentNamePattern().IsMatch(newName))
                return Results.Json(new { error = "Invalid new name" }, statusCode: 400);

            var envSettings = app.Services.GetRequiredService<EnvironmentSettings>();
            var globalPath = Path.Combine(Directory.GetCurrentDirectory(), "environments", "settings.json");
            var oldDir = Path.Combine(Directory.GetCurrentDirectory(), "environments", name);
            var newDir = Path.Combine(Directory.GetCurrentDirectory(), "environments", newName);

            if (!envSettings.AllowedEnvironments.Contains(name, StringComparer.OrdinalIgnoreCase))
                return Results.Json(new { error = "Environment not found" }, statusCode: 404);
            if (envSettings.AllowedEnvironments.Contains(newName, StringComparer.OrdinalIgnoreCase))
                return Results.Json(new { error = "An environment with that name already exists" }, statusCode: 409);

            if (Directory.Exists(oldDir)) Directory.Move(oldDir, newDir);

            var newAllowed = envSettings.AllowedEnvironments
                .Select(e => e.Equals(name, StringComparison.OrdinalIgnoreCase) ? newName : e).ToList();
            var globalModel = new { Environment = new { ServerName = envSettings.ServerName, AllowedEnvironments = newAllowed } };
            await AtomicFile.WriteAllTextAsync(globalPath, JsonSerializer.Serialize(globalModel, SharedJsonOptions.Indented));
            envSettings.Reload();

            Audit(context, "rename", "environment", $"{name} -> {newName}");
            return Results.Ok(new { ok = true, name = newName });
        }).AddEndpointFilter(SerializeEnvironmentListEdit).ExcludeFromDescription();

        app.MapDelete("/_/api/environments/{name}", async (string name, HttpRequest request) =>
        {
            if (!EnvironmentNamePattern().IsMatch(name))
                return Results.Json(new { error = "Invalid environment name" }, statusCode: 400);

            var envSettings = app.Services.GetRequiredService<EnvironmentSettings>();
            var globalPath = Path.Combine(Directory.GetCurrentDirectory(), "environments", "settings.json");
            var envDir = Path.Combine(Directory.GetCurrentDirectory(), "environments", name);
            var deleteFiles = request.Query["delete_files"] == "true";
            var newAllowed = envSettings.AllowedEnvironments
                .Where(e => !e.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
            var backupPath = Services.Configuration.ConfigBackupService.Backup(Path.Combine(envDir, "settings.json"));
            var globalModel = new { Environment = new { ServerName = envSettings.ServerName, AllowedEnvironments = newAllowed } };
            await AtomicFile.WriteAllTextAsync(globalPath,
                JsonSerializer.Serialize(globalModel, SharedJsonOptions.Indented));
            envSettings.Reload();
            if (deleteFiles && Directory.Exists(envDir))
                Directory.Delete(envDir, true);
            Audit(request.HttpContext, "delete", "environment", name, deleteFiles ? "files deleted" : "files kept", backupPath);
            return Results.Ok(new { ok = true });
        }).AddEndpointFilter(SerializeEnvironmentListEdit).ExcludeFromDescription();

    }

    [GeneratedRegex(@"^[a-zA-Z0-9_-]+$")]
    private static partial Regex EnvironmentNamePattern();

    private static bool RemoveSecret(System.Text.Json.Nodes.JsonObject secrets, string secret)
    {
        var existing = secrets.Select(p => p.Key).Where(k => k.Equals(secret, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var key in existing)
            secrets.Remove(key);
        return existing.Count > 0;
    }

    /// <summary>
    /// Encrypts a plaintext ConnectionString and every plaintext Secrets value unless the file sets Encrypt false. True when a value changed
    /// </summary>
    internal static bool EncryptAtRest(System.Text.Json.Nodes.JsonObject settings)
    {
        if (settings["Encrypt"] is System.Text.Json.Nodes.JsonValue optOut && optOut.TryGetValue<bool>(out var encrypt) && !encrypt)
            return false;

        var changed = false;
        if (settings["ConnectionString"] is System.Text.Json.Nodes.JsonValue cs && cs.TryGetValue<string>(out var connection)
            && !string.IsNullOrEmpty(connection) && !SettingsEncryptionHelper.IsEncrypted(connection))
        {
            settings["ConnectionString"] = SettingsEncryptionHelper.Encrypt(connection);
            changed = true;
        }

        if (settings["Secrets"] is System.Text.Json.Nodes.JsonObject secrets)
        {
            foreach (var (key, value) in secrets.ToList())
            {
                if (value is System.Text.Json.Nodes.JsonValue v && v.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) && !SettingsEncryptionHelper.IsEncrypted(text))
                {
                    secrets[key] = SettingsEncryptionHelper.Encrypt(text);
                    changed = true;
                }
            }
        }
        return changed;
    }
}
