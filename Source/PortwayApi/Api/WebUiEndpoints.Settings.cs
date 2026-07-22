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
    private static void MapSettingsRoutes(WebApplication app, PortwayApi.Services.Configuration.ConfigAuditService configAudit)
    {
        void Audit(HttpContext ctx, string action, string targetType, string target, string? details = null, string? backupPath = null)
            => configAudit.Record(action, targetType, target, ctx.Connection.RemoteIpAddress?.ToString(), details, backupPath);

        app.MapGet("/ui/api/settings", async (IConfiguration config, PortwayApi.Services.Mcp.McpConfigService? mcpConfig, PortwayApi.Services.Database.DatabaseMaintenanceService? dbMaintenance) =>
        {
            PortwayApi.Services.Mcp.McpConfigService.ConfigSnapshot? chatCfg = null;
            if (mcpConfig is not null)
            {
                try { chatCfg = await mcpConfig.GetConfigAsync(); }
                catch { /* non-fatal — chat section will show defaults */ }
            }

            var adminKey = config.GetValue<string>("WebUi:AdminApiKey", "") ?? "";
            var corsOriginsCount   = config.GetSection("WebUi:CorsOrigins").Get<string[]>()?.Length ?? 0;
            var publicOriginsCount = config.GetSection("WebUi:PublicOrigins").Get<string[]>()?.Length ?? 0;
            var trustedProxyCount  = (config.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>()?.Length ?? 0)
                                   + (config.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>()?.Length ?? 0);
            var inContainer = string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase);
            var useHttpsEnv = Environment.GetEnvironmentVariable("Use_HTTPS");
            var httpsOn = inContainer
                ? string.Equals(useHttpsEnv, "true", StringComparison.OrdinalIgnoreCase)
                : !string.Equals(useHttpsEnv, "false", StringComparison.OrdinalIgnoreCase);

            return Results.Json(new
        {
            database_maintenance = new
            {
                enabled      = config.GetValue<bool>("DatabaseMaintenance:Enabled", true),
                schedule     = config.GetValue<string>("DatabaseMaintenance:Schedule") ?? "03:00",
                last_run_utc = dbMaintenance?.LastRunUtc,
                last_results = dbMaintenance?.LastRunResults.Select(r => new
                {
                    database = r.Database, vacuumed = r.Vacuumed,
                    reclaimed_bytes = r.BytesBefore - r.BytesAfter, skip_reason = r.SkipReason
                })
            },
            security = new
            {
                webui_auth_enabled = !string.IsNullOrEmpty(adminKey) && adminKey != "INSECURE-CHANGE-ME-admin-api-key",
                admin_key_strength = string.IsNullOrEmpty(adminKey) ? "not-set"
                    : adminKey == "INSECURE-CHANGE-ME-admin-api-key" ? "placeholder"
                    : adminKey.Length < 32 ? "weak" : "strong",
                https_enabled       = httpsOn,
                secure_cookies      = config.GetValue<bool>("WebUi:SecureCookies", false),
                cors_origins_count  = corsOriginsCount,
                public_origins_count = publicOriginsCount,
                trusted_proxies_configured = trustedProxyCount > 0,
                csrf_protection     = true
            },
            rate_limiting = new
            {
                enabled              = config.GetValue<bool>("RateLimiting:Enabled"),
                ip_limit             = config.GetValue<int>("RateLimiting:IpLimit"),
                ip_window_seconds    = config.GetValue<int>("RateLimiting:IpWindow"),
                token_limit          = config.GetValue<int>("RateLimiting:TokenLimit"),
                token_window_seconds = config.GetValue<int>("RateLimiting:TokenWindow")
            },
            caching = new
            {
                enabled                  = config.GetValue<bool>("Caching:Enabled"),
                provider                 = config.GetValue<string>("Caching:ProviderType") ?? "Memory",
                default_duration_seconds = config.GetValue<int>("Caching:DefaultCacheDurationSeconds"),
                max_items                = config.GetValue<int>("Caching:MemoryCacheMaxItems"),
                max_size_mb              = config.GetValue<int>("Caching:MemoryCacheSizeLimitMB")
            },
            sql_pooling = new
            {
                enabled            = config.GetValue<bool>("SqlConnectionPooling:Enabled"),
                min_pool_size      = config.GetValue<int>("SqlConnectionPooling:MinPoolSize"),
                max_pool_size      = config.GetValue<int>("SqlConnectionPooling:MaxPoolSize"),
                connection_timeout = config.GetValue<int>("SqlConnectionPooling:ConnectionTimeout"),
                command_timeout    = config.GetValue<int>("SqlConnectionPooling:CommandTimeout")
            },
            file_storage = new
            {
                directory        = config.GetValue<string>("FileStorage:StorageDirectory") ?? "",
                max_file_size_mb = config.GetValue<long>("FileStorage:MaxFileSizeBytes") / 1024 / 1024
            },
            logging = new
            {
                min_level = config.GetSection("Serilog:MinimumLevel:Default").Value ?? "Information",
                sinks     = config.GetSection("Serilog:WriteTo").GetChildren()
                                .Select(s => s["Name"] ?? "")
                                .Where(n => !string.IsNullOrEmpty(n))
                                .ToList()
            },
            endpoint_reloading = new
            {
                enabled     = config.GetValue<bool>("EndpointReloading:Enabled"),
                debounce_ms = config.GetValue<int>("EndpointReloading:DebounceMs")
            },
            mcp = new
            {
                enabled               = config.GetValue<bool>("Mcp:Enabled"),
                path                  = config.GetValue<string>("Mcp:Path") ?? "/mcp",
                require_authentication = config.GetValue<bool>("Mcp:RequireAuthentication"),
                apps_enabled          = config.GetValue<bool>("Mcp:AppsEnabled", true),
                apps_path             = "/mcp/apps"
            },
            chat = new
            {
                enabled    = config.GetValue<bool>("Mcp:ChatEnabled"),
                configured = chatCfg?.IsConfigured ?? false,
                provider   = chatCfg?.Provider ?? string.Empty,
                model      = chatCfg?.Model ?? string.Empty
            }
        });
        }).ExcludeFromDescription();

        // MCP Configuration endpoints
        // Returns masked status (never returns the raw API key)
        app.MapGet("/ui/api/mcp/config", async (PortwayApi.Services.Mcp.McpConfigService mcpConfig) =>
        {
            var status = await mcpConfig.GetStatusAsync();
            return Results.Json(status);
        }).ExcludeFromDescription();

        // Saves provider/model/apiKey/internalApiToken to the encrypted DB
        // Accepts partial updates; omit a field to leave it unchanged
        app.MapPost("/ui/api/mcp/config", async (
            HttpRequest request,
            PortwayApi.Services.Mcp.McpConfigService mcpConfig) =>
        {
            System.Text.Json.Nodes.JsonNode? body = null;
            try { body = await System.Text.Json.Nodes.JsonNode.ParseAsync(request.Body); }
            catch { return Results.BadRequest(new { error = "Invalid JSON body" }); }

            var provider         = body?["provider"]?.GetValue<string>();
            var model            = body?["model"]?.GetValue<string>();
            var apiKey           = body?["apiKey"]?.GetValue<string>();
            var internalApiToken = body?["internalApiToken"]?.GetValue<string>();

            if (provider is not null && string.IsNullOrWhiteSpace(provider))
                return Results.BadRequest(new { error = "provider cannot be blank" });
            if (apiKey is not null && string.IsNullOrWhiteSpace(apiKey))
                return Results.BadRequest(new { error = "apiKey cannot be blank" });

            await mcpConfig.SaveConfigAsync(provider, model, apiKey, internalApiToken);
            Audit(request.HttpContext, "update", "mcp-config", "chat configuration",
                provider is not null ? $"provider={provider}" : null);
            return Results.Ok(new { ok = true });
        }).ExcludeFromDescription();

        // Clears all stored MCP chat configuration (provider, model, key, token)
        app.MapDelete("/ui/api/mcp/config", async (
            HttpContext context,
            PortwayApi.Services.Mcp.McpConfigService mcpConfig,
            CancellationToken ct) =>
        {
            await mcpConfig.ClearConfigAsync(ct);
            Audit(context, "delete", "mcp-config", "chat configuration");
            return Results.Ok(new { ok = true });
        }).ExcludeFromDescription();

        // Change-controls: audit trail of UI config changes
    }
}
