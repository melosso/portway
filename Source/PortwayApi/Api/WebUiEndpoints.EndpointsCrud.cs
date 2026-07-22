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
    private static void MapEndpointCrudRoutes(WebApplication app, PortwayApi.Services.Configuration.ConfigAuditService configAudit)
    {
        void Audit(HttpContext ctx, string action, string targetType, string target, string? details = null, string? backupPath = null)
            => configAudit.Record(action, targetType, target, ctx.Connection.RemoteIpAddress?.ToString(), details, backupPath);

        app.MapMethods("/ui/api/endpoints/{type}/{**name}", ["PATCH"], async (string type, string name, HttpContext context) =>
        {
            var (filePath, err) = ResolveEndpointPath(type, name);
            if (err != null) return Results.Json(new { error = err }, statusCode: 400);
            if (!File.Exists(filePath!))
                return Results.Json(new { error = "Endpoint not found" }, statusCode: 404);

            var body = await context.Request.ReadFromJsonAsync<JsonElement>();
            var newName = body.TryGetProperty("new_name", out var nn) ? nn.GetString()?.Trim() ?? "" : "";
            if (!System.Text.RegularExpressions.Regex.IsMatch(newName, @"^[a-zA-Z0-9_-]+(/[a-zA-Z0-9_-]+)*$"))
                return Results.Json(new { error = "Invalid name" }, statusCode: 400);

            var (newFilePath, newErr) = ResolveEndpointPath(type, newName);
            if (newErr != null) return Results.Json(new { error = newErr }, statusCode: 400);
            if (File.Exists(newFilePath!))
                return Results.Json(new { error = "An endpoint with that name already exists" }, statusCode: 409);

            var oldDir        = Path.GetDirectoryName(filePath!)!;
            var oldFolderName = Path.GetFileName(oldDir);
            var newDir        = Path.GetDirectoryName(newFilePath!)!;
            Directory.CreateDirectory(Path.GetDirectoryName(newDir)!);
            Directory.Move(oldDir, newDir);

            // If the entity.json Namespace equals the old folder name (doubled-key pattern, e.g. Accounts/Accounts),
            // clear it so the routing key after rename is simply newName instead of "Accounts/newName"
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
                        JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true }));
                }
            }
            catch (Exception ex) { Log.Warning(ex, "Renamed endpoint to {NewName} but could not clear doubled Namespace from entity.json", newName); }

            var epType = TypeStringToEndpointType(type);
            if (epType.HasValue) EndpointHandler.ReloadEndpointType(epType.Value);

            Audit(context, "rename", "endpoint", $"{type}/{name} -> {type}/{actualName}");
            return Results.Ok(new { ok = true, name = actualName });
        }).ExcludeFromDescription();

        // GET /ui/api/endpoints/{type}/{**name}?raw=true , fetch a single endpoint file (structured or raw JSON)
        app.MapGet("/ui/api/endpoints/{type}/{**name}", (string type, string name, HttpRequest request) =>
        {
            var (filePath, err) = ResolveEndpointPath(type, name);
            if (err != null) return Results.Json(new { error = err }, statusCode: 400);

            if (!File.Exists(filePath!))
                return Results.Json(new { error = "Endpoint not found" }, statusCode: 404);

            try
            {
                var raw     = File.ReadAllText(filePath!);
                var lastMod = new DateTimeOffset(File.GetLastWriteTimeUtc(filePath!), TimeSpan.Zero).ToUnixTimeSeconds();

                if (request.Query["raw"] == "true")
                    return Results.Json(new { content = raw, last_modified = lastMod });

                var content = JsonDocument.Parse(raw).RootElement.Clone();
                return Results.Json(new { name, type, content, last_modified = lastMod, raw });
            }
            catch (Exception ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 500);
            }
        }).ExcludeFromDescription();

        // PUT /ui/api/endpoints/{type}/{**name}?raw=true , overwrite a single endpoint file
        app.MapPut("/ui/api/endpoints/{type}/{**name}", async (string type, string name, HttpContext context) =>
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

            // Validate JSON and re-serialize prettified
            try
            {
                using var doc = JsonDocument.Parse(rawContent);
                rawContent = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            }
            catch (JsonException ex) { return Results.Json(new { error = $"Invalid JSON: {ex.Message}" }, statusCode: 400); }

            Directory.CreateDirectory(Path.GetDirectoryName(filePath!)!);
            var backupPath = PortwayApi.Services.Configuration.ConfigBackupService.Backup(filePath!);
            await File.WriteAllTextAsync(filePath!, rawContent);
            var lastMod = new DateTimeOffset(File.GetLastWriteTimeUtc(filePath!), TimeSpan.Zero).ToUnixTimeSeconds();

            var epType = TypeStringToEndpointType(type);
            if (epType.HasValue) EndpointHandler.ReloadEndpointType(epType.Value);

            Audit(context, "update", "endpoint", $"{type}/{name}", null, backupPath);
            return Results.Ok(new { ok = true, last_modified = lastMod });
        }).ExcludeFromDescription();

        // POST /ui/api/endpoints/{type} , create a new endpoint
        app.MapPost("/ui/api/endpoints/{type}", async (string type, HttpContext context) =>
        {
            var body = await context.Request.ReadFromJsonAsync<JsonElement>();
            var name = body.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";

            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z0-9_-]+(/[a-zA-Z0-9_-]+)*$"))
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

            Directory.CreateDirectory(Path.GetDirectoryName(filePath!)!);
            await File.WriteAllTextAsync(filePath!, rawContent);

            var epType = TypeStringToEndpointType(type);
            if (epType.HasValue) EndpointHandler.ReloadEndpointType(epType.Value);

            Audit(context, "create", "endpoint", $"{type}/{name}");
            return Results.Json(new { ok = true, name }, statusCode: 201);
        }).ExcludeFromDescription();

        // DELETE /ui/api/endpoints/{type}/{**name} , remove an endpoint
        app.MapDelete("/ui/api/endpoints/{type}/{**name}", (string type, string name, HttpContext context) =>
        {
            var (filePath, err) = ResolveEndpointPath(type, name);
            if (err != null) return Results.Json(new { error = err }, statusCode: 400);

            if (!File.Exists(filePath!))
                return Results.Json(new { error = "Endpoint not found" }, statusCode: 404);

            var backupPath = PortwayApi.Services.Configuration.ConfigBackupService.Backup(filePath!);
            var dir = Path.GetDirectoryName(filePath!);
            if (dir != null && Directory.Exists(dir))
                Directory.Delete(dir, true);
            else
                File.Delete(filePath!);

            var epType = TypeStringToEndpointType(type);
            if (epType.HasValue) EndpointHandler.ReloadEndpointType(epType.Value);

            Audit(context, "delete", "endpoint", $"{type}/{name}", null, backupPath);
            return Results.Ok(new { ok = true });
        }).ExcludeFromDescription();

        // POST /ui/api/endpoints/{type}/validate , dry-run configuration check without saving
        app.MapPost("/ui/api/endpoints/{type}/validate", async (string type, HttpContext context) =>
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

            // Same namespace rules the loader enforces at startup (charset, length, reserved names)
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("Namespace", out var ns) && ns.ValueKind == JsonValueKind.String)
            {
                errors.AddRange(PortwayApi.Helpers.DirectoryHelper.ValidateNamespaceName(ns.GetString() ?? ""));
            }

            return Results.Json(new { valid = errors.Count == 0, errors });
        }).ExcludeFromDescription();

        // Receive and log client-side JS errors for production visibility
        // Exempt from auth (sendBeacon fires from any page state); rate-limited by the IP limiter
    }
}
