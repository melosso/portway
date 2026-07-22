namespace PortwayApi.Endpoints;

using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using Serilog;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PortwayApi.Auth;
using PortwayApi.Classes;
using PortwayApi.Helpers;
using PortwayApi.Interfaces;
using PortwayApi.Services;


public static partial class WebUiEndpointExtensions
{
    private static void MapEnvironmentRoutes(WebApplication app, PortwayApi.Services.Configuration.ConfigAuditService configAudit)
    {
        void Audit(HttpContext ctx, string action, string targetType, string target, string? details = null, string? backupPath = null)
            => configAudit.Record(action, targetType, target, ctx.Connection.RemoteIpAddress?.ToString(), details, backupPath);

        app.MapGet("/ui/api/environments", () =>
        {
            var envSettings  = app.Services.GetRequiredService<EnvironmentSettings>();
            var globalPath   = Path.Combine(Directory.GetCurrentDirectory(), "environments", "settings.json");
            var lastModified = File.Exists(globalPath)
                ? new DateTimeOffset(File.GetLastWriteTimeUtc(globalPath), TimeSpan.Zero).ToUnixTimeSeconds()
                : 0L;
            return Results.Json(new
            {
                server_name          = envSettings.ServerName,
                allowed_environments = envSettings.AllowedEnvironments,
                last_modified        = lastModified
            });
        }).ExcludeFromDescription();

        // Environment CRUD
        app.MapPut("/ui/api/environments/settings", async (HttpContext context) =>
        {
            var body        = await context.Request.ReadFromJsonAsync<JsonElement>();
            var envSettings = app.Services.GetRequiredService<EnvironmentSettings>();
            var globalPath  = Path.Combine(Directory.GetCurrentDirectory(), "environments", "settings.json");

            var serverName  = body.TryGetProperty("server_name", out var sn) && sn.ValueKind == JsonValueKind.String
                ? sn.GetString() ?? envSettings.ServerName : envSettings.ServerName;
            var allowedEnvs = body.TryGetProperty("allowed_environments", out var ae) && ae.ValueKind == JsonValueKind.Array
                ? ae.EnumerateArray().Select(e => e.GetString() ?? "").Where(e => !string.IsNullOrEmpty(e)).ToList()
                : envSettings.AllowedEnvironments;

            var backupPath = PortwayApi.Services.Configuration.ConfigBackupService.Backup(globalPath);
            var model = new { Environment = new { ServerName = serverName, AllowedEnvironments = allowedEnvs } };
            await File.WriteAllTextAsync(globalPath, JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true }));
            envSettings.Reload();
            Audit(context, "update", "environment-settings", "environments/settings.json", null, backupPath);
            return Results.Ok(new { ok = true });
        }).ExcludeFromDescription();

        app.MapGet("/ui/api/environments/{name}", (string name) =>
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z0-9_-]+$"))
                return Results.Json(new { error = "Invalid environment name" }, statusCode: 400);

            var envPath = Path.Combine(Directory.GetCurrentDirectory(), "environments", name, "settings.json");
            if (!File.Exists(envPath))
                return Results.Json(new
                {
                    name, exists = false, server_name = (string?)null,
                    connection_string = (string?)null, connection_string_encrypted = false,
                    headers = new Dictionary<string, string>(), last_modified = 0L
                });

            try
            {
                var json  = File.ReadAllText(envPath);
                using var doc = JsonDocument.Parse(json);
                var root  = doc.RootElement;
                var cs    = root.TryGetProperty("ConnectionString", out var csEl) ? csEl.GetString() : null;
                var sn    = root.TryGetProperty("ServerName",       out var snEl) ? snEl.GetString() : null;
                var hdrs  = new Dictionary<string, string>();
                if (root.TryGetProperty("Headers", out var hEl) && hEl.ValueKind == JsonValueKind.Object)
                    foreach (var h in hEl.EnumerateObject())
                        hdrs[h.Name] = h.Value.GetString() ?? "";
                var lastMod = new DateTimeOffset(File.GetLastWriteTimeUtc(envPath), TimeSpan.Zero).ToUnixTimeSeconds();
                var isEncrypted = cs != null && cs.StartsWith("PWENC:");
                return Results.Json(new
                {
                    name, exists = true, server_name = sn,
                    connection_string = isEncrypted ? null : cs,
                    connection_string_encrypted = isEncrypted,
                    headers = hdrs, last_modified = lastMod
                });
            }
            catch (Exception ex)
            {
                return Results.Json(new { error = $"Failed to read settings: {ex.Message}" }, statusCode: 500);
            }
        }).ExcludeFromDescription();

        app.MapGet("/ui/api/environments/{name}/raw", (string name) =>
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z0-9_-]+$"))
                return Results.Json(new { error = "Invalid environment name" }, statusCode: 400);

            var envPath = Path.Combine(Directory.GetCurrentDirectory(), "environments", name, "settings.json");
            if (!File.Exists(envPath))
                return Results.Json(new { error = "File not found" }, statusCode: 404);

            try
            {
                var raw     = File.ReadAllText(envPath);
                var lastMod = new DateTimeOffset(File.GetLastWriteTimeUtc(envPath), TimeSpan.Zero).ToUnixTimeSeconds();
                return Results.Json(new { content = raw, last_modified = lastMod });
            }
            catch (Exception ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 500);
            }
        }).ExcludeFromDescription();

        app.MapPut("/ui/api/environments/{name}/raw", async (string name, HttpContext context) =>
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z0-9_-]+$"))
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

            // Automagicallyy encrypt any plaintext ConnectionString so raw saves never bypass encryptionn
            if (root.TryGetProperty("ConnectionString", out var csEl) &&
                csEl.ValueKind == JsonValueKind.String)
            {
                var cs = csEl.GetString() ?? "";
                if (!string.IsNullOrEmpty(cs) && !cs.StartsWith("PWENC:"))
                {
                    var encrypted = PortwayApi.Helpers.SettingsEncryptionHelper.Encrypt(cs);
                    // Rebuild JSON with encrypted value
                    var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(raw)!;
                    var rebuilt = new Dictionary<string, object?>();
                    foreach (var (k, v) in dict)
                        rebuilt[k] = k == "ConnectionString"
                            ? (object?)encrypted
                            : JsonSerializer.Deserialize<object>(v.GetRawText());
                    raw = JsonSerializer.Serialize(rebuilt, new JsonSerializerOptions { WriteIndented = true });
                }
            }

            var envPath = Path.Combine(Directory.GetCurrentDirectory(), "environments", name, "settings.json");
            Directory.CreateDirectory(Path.GetDirectoryName(envPath)!);
            var backupPath = PortwayApi.Services.Configuration.ConfigBackupService.Backup(envPath);
            await File.WriteAllTextAsync(envPath, raw);
            Audit(context, "update-raw", "environment", name, null, backupPath);
            var lastMod = new DateTimeOffset(File.GetLastWriteTimeUtc(envPath), TimeSpan.Zero).ToUnixTimeSeconds();
            return Results.Ok(new { ok = true, last_modified = lastMod });
        }).ExcludeFromDescription();

        app.MapPost("/ui/api/environments/{name}/test", async (string name) =>
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z0-9_-]+$"))
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
                cmd.CommandText  = sqlProvider.HealthCheckQuery;
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

        app.MapPost("/ui/api/environments", async (HttpContext context) =>
        {
            var body        = await context.Request.ReadFromJsonAsync<JsonElement>();
            var name        = body.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var envSettings = app.Services.GetRequiredService<EnvironmentSettings>();
            var globalPath  = Path.Combine(Directory.GetCurrentDirectory(), "environments", "settings.json");

            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z0-9_-]+$"))
                return Results.Json(new { error = "Invalid environment name. Use only letters, numbers, hyphens, and underscores." }, statusCode: 400);
            if (envSettings.AllowedEnvironments.Contains(name, StringComparer.OrdinalIgnoreCase))
                return Results.Json(new { error = "Environment already exists" }, statusCode: 409);

            var envDir         = Path.Combine(Directory.GetCurrentDirectory(), "environments", name);
            var envSettingsPath = Path.Combine(envDir, "settings.json");
            Directory.CreateDirectory(envDir);

            var serverName = body.TryGetProperty("server_name", out var sn) && sn.ValueKind == JsonValueKind.String
                ? sn.GetString() ?? envSettings.ServerName : envSettings.ServerName;
            var connStr    = body.TryGetProperty("connection_string", out var cs) && cs.ValueKind == JsonValueKind.String
                ? cs.GetString() ?? "" : "";
            var headers    = new Dictionary<string, string>();
            if (body.TryGetProperty("headers", out var hdrs) && hdrs.ValueKind == JsonValueKind.Object)
                foreach (var h in hdrs.EnumerateObject())
                    headers[h.Name] = h.Value.GetString() ?? "";

            var encryptedCs = !string.IsNullOrEmpty(connStr)
                ? PortwayApi.Helpers.SettingsEncryptionHelper.Encrypt(connStr) : "";
            var envModel = new { ConnectionString = encryptedCs, ServerName = serverName, Headers = headers };
            await File.WriteAllTextAsync(envSettingsPath,
                JsonSerializer.Serialize(envModel, new JsonSerializerOptions { WriteIndented = true }));

            var newAllowed = envSettings.AllowedEnvironments;
            newAllowed.Add(name);
            var globalModel = new { Environment = new { ServerName = envSettings.ServerName, AllowedEnvironments = newAllowed } };
            await File.WriteAllTextAsync(globalPath,
                JsonSerializer.Serialize(globalModel, new JsonSerializerOptions { WriteIndented = true }));
            envSettings.Reload();
            Audit(context, "create", "environment", name);
            return Results.Json(new { ok = true, name }, statusCode: 201);
        }).ExcludeFromDescription();

        app.MapPut("/ui/api/environments/{name}", async (string name, HttpContext context) =>
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z0-9_-]+$"))
                return Results.Json(new { error = "Invalid environment name" }, statusCode: 400);

            var envDir          = Path.Combine(Directory.GetCurrentDirectory(), "environments", name);
            var envSettingsPath = Path.Combine(envDir, "settings.json");
            var existingJson    = File.Exists(envSettingsPath) ? File.ReadAllText(envSettingsPath) : "{}";
            JsonElement existing;
            try { existing = JsonDocument.Parse(existingJson).RootElement.Clone(); }
            catch { existing = JsonDocument.Parse("{}").RootElement.Clone(); }

            var body       = await context.Request.ReadFromJsonAsync<JsonElement>();
            var serverName = body.TryGetProperty("server_name", out var sn) && sn.ValueKind == JsonValueKind.String
                ? sn.GetString()
                : (existing.TryGetProperty("ServerName", out var esn) ? esn.GetString() : null);

            var existingCs = existing.TryGetProperty("ConnectionString", out var ecs) ? ecs.GetString() ?? "" : "";
            string newCs;
            if (body.TryGetProperty("connection_string", out var cs) && cs.ValueKind == JsonValueKind.String &&
                !string.IsNullOrEmpty(cs.GetString()))
                newCs = PortwayApi.Helpers.SettingsEncryptionHelper.Encrypt(cs.GetString()!);
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
            var backupPath = PortwayApi.Services.Configuration.ConfigBackupService.Backup(envSettingsPath);
            var envModel = new { ConnectionString = newCs, ServerName = serverName, Headers = headers };
            await File.WriteAllTextAsync(envSettingsPath,
                JsonSerializer.Serialize(envModel, new JsonSerializerOptions { WriteIndented = true }));
            Audit(context, "update", "environment", name, null, backupPath);
            var lastMod = new DateTimeOffset(File.GetLastWriteTimeUtc(envSettingsPath), TimeSpan.Zero).ToUnixTimeSeconds();
            return Results.Ok(new { ok = true, last_modified = lastMod });
        }).ExcludeFromDescription();

        app.MapMethods("/ui/api/environments/{name}", ["PATCH"], async (string name, HttpContext context) =>
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z0-9_-]+$"))
                return Results.Json(new { error = "Invalid environment name" }, statusCode: 400);

            var body = await context.Request.ReadFromJsonAsync<JsonElement>();
            var newName = body.TryGetProperty("new_name", out var nn) ? nn.GetString()?.Trim() ?? "" : "";
            if (!System.Text.RegularExpressions.Regex.IsMatch(newName, @"^[a-zA-Z0-9_-]+$"))
                return Results.Json(new { error = "Invalid new name" }, statusCode: 400);

            var envSettings = app.Services.GetRequiredService<EnvironmentSettings>();
            var globalPath  = Path.Combine(Directory.GetCurrentDirectory(), "environments", "settings.json");
            var oldDir      = Path.Combine(Directory.GetCurrentDirectory(), "environments", name);
            var newDir      = Path.Combine(Directory.GetCurrentDirectory(), "environments", newName);

            if (!envSettings.AllowedEnvironments.Contains(name, StringComparer.OrdinalIgnoreCase))
                return Results.Json(new { error = "Environment not found" }, statusCode: 404);
            if (envSettings.AllowedEnvironments.Contains(newName, StringComparer.OrdinalIgnoreCase))
                return Results.Json(new { error = "An environment with that name already exists" }, statusCode: 409);

            if (Directory.Exists(oldDir)) Directory.Move(oldDir, newDir);

            var newAllowed  = envSettings.AllowedEnvironments
                .Select(e => e.Equals(name, StringComparison.OrdinalIgnoreCase) ? newName : e).ToList();
            var globalModel = new { Environment = new { ServerName = envSettings.ServerName, AllowedEnvironments = newAllowed } };
            await File.WriteAllTextAsync(globalPath, JsonSerializer.Serialize(globalModel, new JsonSerializerOptions { WriteIndented = true }));
            envSettings.Reload();

            Audit(context, "rename", "environment", $"{name} -> {newName}");
            return Results.Ok(new { ok = true, name = newName });
        }).ExcludeFromDescription();

        app.MapDelete("/ui/api/environments/{name}", async (string name, HttpRequest request) =>
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z0-9_-]+$"))
                return Results.Json(new { error = "Invalid environment name" }, statusCode: 400);

            var envSettings  = app.Services.GetRequiredService<EnvironmentSettings>();
            var globalPath   = Path.Combine(Directory.GetCurrentDirectory(), "environments", "settings.json");
            var envDir       = Path.Combine(Directory.GetCurrentDirectory(), "environments", name);
            var deleteFiles  = request.Query["delete_files"] == "true";
            var newAllowed   = envSettings.AllowedEnvironments
                .Where(e => !e.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
            var backupPath   = PortwayApi.Services.Configuration.ConfigBackupService.Backup(Path.Combine(envDir, "settings.json"));
            var globalModel  = new { Environment = new { ServerName = envSettings.ServerName, AllowedEnvironments = newAllowed } };
            await File.WriteAllTextAsync(globalPath,
                JsonSerializer.Serialize(globalModel, new JsonSerializerOptions { WriteIndented = true }));
            envSettings.Reload();
            if (deleteFiles && Directory.Exists(envDir))
                Directory.Delete(envDir, true);
            Audit(request.HttpContext, "delete", "environment", name, deleteFiles ? "files deleted" : "files kept", backupPath);
            return Results.Ok(new { ok = true });
        }).ExcludeFromDescription();

    }
}
