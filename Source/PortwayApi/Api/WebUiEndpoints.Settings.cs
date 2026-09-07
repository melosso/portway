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
using Microsoft.EntityFrameworkCore;
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

        app.MapGet("/ui/api/settings", async (IConfiguration config, PortwayApi.Services.Telemetry.TelemetryOptions telemetry, PortwayApi.Services.Mcp.McpConfigService? mcpConfig, PortwayApi.Services.Database.DatabaseMaintenanceService? dbMaintenance, PortwayApi.Auth.AdminUserService users, PortwayApi.Auth.AuthDbContext db, HttpContext ctx) =>
        {
            PortwayApi.Services.Mcp.McpConfigService.ConfigSnapshot? chatCfg = null;
            if (mcpConfig is not null)
            {
                try { chatCfg = await mcpConfig.GetConfigAsync(); }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Failed to read MCP chat configuration for the settings page");
                }
            }

            var adminKey = config.GetValue<string>("WebUi:AdminApiKey", "") ?? "";
            var accountCount = await users.CountAsync();
            var corsOriginsCount   = config.GetSection("WebUi:CorsOrigins").Get<string[]>()?.Length ?? 0;
            var publicOrigins      = config.GetSection("WebUi:PublicOrigins").Get<string[]>() ?? [];
            var knownProxies       = config.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
            var knownNetworks      = config.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [];
            var trustedProxyCount  = knownProxies.Length + knownNetworks.Length;

            // What the console gate actually compared for this request, so the page can say whether
            // the deployment is reporting real client addresses or the reverse proxy's own
            var peerIp        = ctx.Connection.RemoteIpAddress;
            var forwardedFor  = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault() ?? "";
            var behindProxy   = !string.IsNullOrEmpty(forwardedFor);
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
                webui_auth_enabled = PortwayApi.Helpers.WebUiAuthState.Enabled,
                admin_accounts     = accountCount,
                // The key only ever seeds the first account now; left in place it is a secret with no job
                legacy_admin_key   = !string.IsNullOrEmpty(adminKey),
                https_enabled       = httpsOn,
                secure_cookies      = config.GetValue<bool>("WebUi:SecureCookies", false),
                cors_origins_count  = corsOriginsCount,
                public_origins_count = publicOrigins.Length,
                trusted_proxies_configured = trustedProxyCount > 0,
                csrf_protection     = true,
                client_ip           = peerIp?.ToString() ?? "",
                behind_proxy        = behindProxy,
                // Forwarded headers arriving from an untrusted hop are ignored, so every client looks
                // like the proxy: the console gate, per-IP rate limiting and the login lockout all blur together
                forwarded_ignored   = behindProxy && trustedProxyCount == 0,
                console_public      = publicOrigins.Length > 0
            },
            deployment = new
            {
                public_origins  = publicOrigins,
                known_proxies   = knownProxies,
                known_networks  = knownNetworks
            },
            // The disable group: one place to turn whole subsystems off
            features = new
            {
                oidc            = config.GetValue("Oidc:Enabled", true),
                openapi         = config.GetValue("OpenApi:Enabled", true),
                traffic_logging = config.GetValue("RequestTrafficLogging:Enabled", false),
                landing_page    = config.GetValue("WebUi:Customization:EnableLandingPage", true),
                oidc_providers  = await db.OidcProviders.CountAsync(p => p.IsEnabled)
            },
            customization = new
            {
                promo_text   = config.GetValue<string>("WebUi:Customization:PromoText") ?? "",
                promo_login  = config.GetValue("WebUi:Customization:PromoLogin", false),
                login_footer = config.GetValue<string>("WebUi:Customization:LoginFooter") ?? ""
            },
            rate_limiting = new
            {
                enabled              = config.GetValue<bool>("RateLimiting:Enabled"),
                store                = config.GetValue<string>("RateLimiting:Store") ?? "Memory",
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
                max_file_size_mb    = config.GetValue<long>("FileStorage:MaxFileSizeBytes") / 1024 / 1024,
                max_file_size_bytes = config.GetValue<long>("FileStorage:MaxFileSizeBytes")
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
            telemetry = new
            {
                provider        = telemetry.EffectiveProvider.ToString(),
                service_name    = telemetry.ServiceName ?? PortwayApi.Services.Telemetry.PortwayTelemetry.ServiceName,
                otlp_endpoint   = telemetry.EffectiveProvider == PortwayApi.Services.Telemetry.TelemetryProvider.Otlp
                                    ? telemetry.EffectiveOtlpEndpoint : null,
                prometheus_path = telemetry.ActiveMetricsPath
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
            },
            writable = PortwayApi.Services.Configuration.SettingsWriteService.Schema
                .Select(w => new
                {
                    key              = w.Key,
                    kind             = w.Kind,
                    requires_restart = w.RequiresRestart,
                    min              = w.Min,
                    max              = w.Max,
                    choices          = w.Choices
                })
        });
        }).ExcludeFromDescription();

        // Applies a whitelisted subset of configuration; everything else is refused by name
        app.MapPut("/ui/api/settings", async (
            HttpContext ctx,
            IConfiguration config,
            PortwayApi.Helpers.UrlValidator urlValidator,
            PortwayApi.Services.Configuration.SettingsWriteService writer) =>
        {
            Dictionary<string, JsonElement>? changes;
            try
            {
                changes = await JsonSerializer.DeserializeAsync<Dictionary<string, JsonElement>>(ctx.Request.Body);
            }
            catch (JsonException)
            {
                return Results.Json(new { error = "Invalid JSON body" }, statusCode: 400);
            }

            if (changes is null || changes.Count == 0)
                return Results.Json(new { error = "No settings supplied" }, statusCode: 400);

            if (changes.Count > 50)
                return Results.Json(new { error = "Too many settings in one request" }, statusCode: 400);

            // Reaching the console is what these three settings decide, so a change that would shut the
            // caller out is refused here rather than discovered after the restart that applies it
            if (WouldLockCallerOut(ctx, config, urlValidator, changes) is { } lockout)
                return Results.Json(new { error = lockout.Message, field = lockout.Field }, statusCode: 400);

            var result = await writer.ApplyAsync(changes);
            if (!result.Ok)
                return Results.Json(new { error = result.Error, field = result.Field }, statusCode: 400);

            Audit(ctx, "update", "settings", string.Join(", ", changes.Keys),
                $"{changes.Count} setting(s) changed");

            Log.Information("Settings updated through the Web UI: {Keys}", string.Join(", ", changes.Keys));

            return Results.Json(new { ok = true, restart_required = result.RestartRequired });
        }).ExcludeFromDescription();

        // Change-controls: audit trail of UI config changes

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

    /// <summary>
    /// Refuses a deployment-shape change that would leave the caller outside the console's own gate.
    /// The gate admits a request whose origin matches PublicOrigins, or whose client IP is local, so
    /// both halves are recomputed against the values the request is asking to store.
    /// </summary>
    private static (string Message, string Field)? WouldLockCallerOut(
        HttpContext ctx,
        IConfiguration config,
        PortwayApi.Helpers.UrlValidator urlValidator,
        IDictionary<string, JsonElement> changes)
    {
        string[] Strings(string key, string[] current) =>
            changes.TryGetValue(key, out var raw) && raw.ValueKind == JsonValueKind.Array
                ? raw.EnumerateArray()
                     .Where(e => e.ValueKind == JsonValueKind.String)
                     .Select(e => (e.GetString() ?? "").Trim())
                     .Where(v => v.Length > 0)
                     .ToArray()
                : current;

        var touchesOrigins = changes.ContainsKey("WebUi:PublicOrigins");
        var touchesProxies = changes.ContainsKey("ForwardedHeaders:KnownProxies")
                          || changes.ContainsKey("ForwardedHeaders:KnownNetworks");
        if (!touchesOrigins && !touchesProxies) return null;

        var origins = Strings("WebUi:PublicOrigins",
            config.GetSection("WebUi:PublicOrigins").Get<string[]>() ?? []);

        if (origins.Length > 0 && IsPublicOriginAllowed(ctx.Request, origins))
            return null;

        var clientIp = EffectiveClientIp(ctx, config, changes);
        if (clientIp is not null && urlValidator.IsClientIpAllowed(clientIp))
            return null;

        var field = touchesOrigins ? "WebUi:PublicOrigins" : "ForwardedHeaders:KnownProxies";
        return ($"This change would refuse your own requests to the console (seen as {clientIp?.ToString() ?? "an unknown address"}). " +
                "Keep an entry that covers you, or edit appsettings.json on the server instead.", field);
    }

    /// <summary>
    /// The address the console gate would compare after the change. Trusting a proxy makes the
    /// forwarded chain authoritative, so the caller stops being the proxy and becomes its client.
    /// </summary>
    private static IPAddress? EffectiveClientIp(
        HttpContext ctx, IConfiguration config, IDictionary<string, JsonElement> changes)
    {
        var peer = ctx.Connection.RemoteIpAddress;
        if (peer is null) return null;
        if (peer.IsIPv4MappedToIPv6) peer = peer.MapToIPv4();

        string[] Strings(string key) =>
            changes.TryGetValue(key, out var raw) && raw.ValueKind == JsonValueKind.Array
                ? raw.EnumerateArray()
                     .Where(e => e.ValueKind == JsonValueKind.String)
                     .Select(e => (e.GetString() ?? "").Trim())
                     .Where(v => v.Length > 0)
                     .ToArray()
                : config.GetSection(key).Get<string[]>() ?? [];

        var trusted = Strings("ForwardedHeaders:KnownProxies")
            .Any(p => IPAddress.TryParse(p, out var ip) && ip.Equals(peer))
            || Strings("ForwardedHeaders:KnownNetworks")
                .Any(n => System.Net.IPNetwork.TryParse(n, out var net) && net.Contains(peer));

        if (!trusted) return peer;

        // ponytail: takes the first hop in the chain, which is the client for the single-proxy case;
        // a multi-proxy chain with only some hops trusted would resolve to a later entry
        var forwarded = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (string.IsNullOrEmpty(forwarded)) return peer;

        var first = forwarded.Split(',')[0].Trim();
        return IPAddress.TryParse(first, out var client) ? client : peer;
    }
}
