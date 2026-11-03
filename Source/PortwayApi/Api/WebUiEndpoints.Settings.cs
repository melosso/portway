namespace PortwayApi.Endpoints;

using System.Net;
using Serilog;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using PortwayApi.Helpers;


public static partial class WebUiEndpointExtensions
{
    /// <summary>
    /// Current value of every writable setting. Excludes write-only settings.
    /// </summary>
    internal static Dictionary<string, object?> SettingValues(IConfiguration config, PortwayApi.Classes.OpenApi.OpenApiSettings openApi, string effectiveTelemetryProvider)
    {
        string[] List(string key) => config.GetSection(key).Get<string[]>() ?? [];

        return new(StringComparer.OrdinalIgnoreCase)
        {
            ["RateLimiting:Enabled"] = config.GetValue<bool>("RateLimiting:Enabled"),
            ["RateLimiting:IpLimit"] = config.GetValue<int>("RateLimiting:IpLimit"),
            ["RateLimiting:IpWindow"] = config.GetValue<int>("RateLimiting:IpWindow"),
            ["RateLimiting:TokenLimit"] = config.GetValue<int>("RateLimiting:TokenLimit"),
            ["RateLimiting:TokenWindow"] = config.GetValue<int>("RateLimiting:TokenWindow"),
            ["Caching:Enabled"] = config.GetValue<bool>("Caching:Enabled"),
            ["Caching:DefaultCacheDurationSeconds"] = config.GetValue<int>("Caching:DefaultCacheDurationSeconds"),
            ["Caching:MemoryCacheSizeLimitMB"] = config.GetValue<int>("Caching:MemoryCacheSizeLimitMB"),
            ["SqlConnectionPooling:Enabled"] = config.GetValue<bool>("SqlConnectionPooling:Enabled"),
            ["SqlConnectionPooling:MinPoolSize"] = config.GetValue<int>("SqlConnectionPooling:MinPoolSize"),
            ["SqlConnectionPooling:MaxPoolSize"] = config.GetValue<int>("SqlConnectionPooling:MaxPoolSize"),
            ["SqlConnectionPooling:ConnectionTimeout"] = config.GetValue<int>("SqlConnectionPooling:ConnectionTimeout"),
            ["SqlConnectionPooling:CommandTimeout"] = config.GetValue<int>("SqlConnectionPooling:CommandTimeout"),
            ["EndpointReloading:Enabled"] = config.GetValue<bool>("EndpointReloading:Enabled"),
            ["EndpointReloading:DebounceMs"] = config.GetValue<int>("EndpointReloading:DebounceMs"),
            ["Serilog:MinimumLevel:Default"] = config["Serilog:MinimumLevel:Default"] ?? "Information",
            ["DatabaseMaintenance:Enabled"] = config.GetValue("DatabaseMaintenance:Enabled", true),
            ["DatabaseMaintenance:Schedule"] = config["DatabaseMaintenance:Schedule"] ?? "03:00",
            ["DatabaseMaintenance:Backup:Enabled"] = config.GetValue("DatabaseMaintenance:Backup:Enabled", true),
            ["DatabaseMaintenance:Backup:Retention"] = config.GetValue("DatabaseMaintenance:Backup:Retention", 7),
            ["Mcp:Enabled"] = config.GetValue<bool>("Mcp:Enabled"),
            ["Mcp:RequireAuthentication"] = config.GetValue<bool>("Mcp:RequireAuthentication"),
            ["Mcp:ChatEnabled"] = config.GetValue<bool>("Mcp:ChatEnabled"),
            ["Console:SecureCookies"] = config.GetValue("Console:SecureCookies", false),
            ["Console:Enabled"] = config.GetValue("Console:Enabled", true),
            ["Console:Customization:EnableLandingPage"] = config.GetValue("Console:Customization:EnableLandingPage", true),
            ["Console:Customization:PromoText"] = config["Console:Customization:PromoText"] ?? "",
            ["Console:Customization:PromoLogin"] = config.GetValue("Console:Customization:PromoLogin", false),
            ["Console:Customization:LoginFooter"] = config["Console:Customization:LoginFooter"] ?? "",
            ["Console:PublicOrigins"] = List("Console:PublicOrigins"),
            ["ForwardedHeaders:KnownProxies"] = List("ForwardedHeaders:KnownProxies"),
            ["ForwardedHeaders:KnownNetworks"] = List("ForwardedHeaders:KnownNetworks"),
            ["Oidc:Enabled"] = config.GetValue("Oidc:Enabled", true),
            ["RequestTrafficLogging:Enabled"] = config.GetValue("RequestTrafficLogging:Enabled", false),
            ["FileStorage:MaxFileSizeBytes"] = config.GetValue<long>("FileStorage:MaxFileSizeBytes"),
            ["Telemetry:Provider"] = config["Telemetry:Provider"] is { Length: > 0 } provider ? provider : effectiveTelemetryProvider,
            ["Telemetry:ServiceName"] = config["Telemetry:ServiceName"] ?? "",
            ["Telemetry:Otlp:Endpoint"] = config["Telemetry:Otlp:Endpoint"] ?? "",
            ["OpenApi:Enabled"] = config.GetValue("OpenApi:Enabled", true),
            ["OpenApi:Title"] = openApi.Title,
            ["OpenApi:Version"] = openApi.Version,
            ["OpenApi:Description"] = openApi.Description,
            ["OpenApi:Contact:Name"] = openApi.Contact.Name,
            ["OpenApi:Contact:Email"] = openApi.Contact.Email,
            ["OpenApi:ForceHttpsInProduction"] = openApi.ForceHttpsInProduction,
            ["OpenApi:ShowNamespaces"] = openApi.ShowNamespaces,
            ["OpenApi:DefaultGroup"] = openApi.DefaultGroup,
            ["OpenApi:ShowBadges"] = openApi.ShowBadges,
            ["OpenApi:ServeMarkdown"] = openApi.ServeMarkdown,
            ["OpenApi:ExternalDocs:Url"] = openApi.ExternalDocs.Url,
            ["OpenApi:ExternalDocs:Description"] = openApi.ExternalDocs.Description,
            ["OpenApi:SecurityDefinition:Description"] = openApi.SecurityDefinition.Description,
            ["OpenApi:Footer:Text"] = openApi.Footer.Text,
            ["OpenApi:Footer:Url"] = openApi.Footer.Url,
            ["OpenApi:Footer:Target"] = openApi.Footer.Target,
            ["OpenApi:Footer:ShowSourceIcon"] = openApi.Footer.ShowSourceIcon,
            ["OpenApi:ScalarTheme"] = openApi.ScalarTheme,
            ["OpenApi:ScalarLayout"] = openApi.ScalarLayout,
            ["OpenApi:ScalarShowSidebar"] = openApi.ScalarShowSidebar,
            ["OpenApi:ScalarHideDownloadButton"] = openApi.ScalarHideDownloadButton,
            ["OpenApi:ScalarHideModels"] = openApi.ScalarHideModels,
            ["OpenApi:ScalarHideClientButton"] = openApi.ScalarHideClientButton,
            ["OpenApi:ScalarHideTestRequestButton"] = openApi.ScalarHideTestRequestButton
        };
    }

    private static void MapSettingsRoutes(WebApplication app, PortwayApi.Services.Configuration.ConfigAuditService configAudit)
    {
        void Audit(HttpContext ctx, string action, string targetType, string target, string? details = null, string? backupPath = null)
            => configAudit.Record(action, targetType, target, ctx.Connection.RemoteIpAddress?.ToString(), details, backupPath);

        app.MapGet("/_/api/settings", async (IConfiguration config, PortwayApi.Services.Configuration.SettingsWriteService writer, PortwayApi.Services.Telemetry.TelemetryOptions telemetry, PortwayApi.Services.Mcp.McpConfigService? mcpConfig, PortwayApi.Services.Database.DatabaseMaintenanceService? dbMaintenance, PortwayApi.Auth.AdminUserService users, PortwayApi.Auth.AuthDbContext db, IOptionsMonitor<PortwayApi.Classes.OpenApi.OpenApiSettings> openApiMonitor, HttpContext ctx) =>
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

            var adminKey = config.GetValue<string>("Console:AdminApiKey", "") ?? "";
            var accountCount = await users.CountAsync();
            var corsOriginsCount = config.GetSection("Console:CorsOrigins").Get<string[]>()?.Length ?? 0;
            var publicOrigins = config.GetSection("Console:PublicOrigins").Get<string[]>() ?? [];
            var knownProxies = config.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
            var knownNetworks = config.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [];
            var trustedProxyCount = knownProxies.Length + knownNetworks.Length;
            var peerIp = ctx.Connection.RemoteIpAddress;
            var forwardedFor = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault() ?? "";
            var behindProxy = !string.IsNullOrEmpty(forwardedFor);
            var values = SettingValues(config, openApiMonitor.CurrentValue, telemetry.EffectiveProvider.ToString());

            return Results.Json(new
            {
                database_maintenance = new
                {
                    last_run_utc = dbMaintenance?.LastRunUtc,
                    last_results = dbMaintenance?.LastRunResults.Select(MaintenanceResultDto)
                },
                security = new
                {
                    webui_auth_enabled = WebUiAuthState.Enabled,
                    admin_accounts = accountCount,
                    // seeding key has no use after the first account
                    legacy_admin_key = !string.IsNullOrEmpty(adminKey),
                    https_enabled = ctx.Request.IsHttps,
                    secure_cookies = config.GetValue<bool>("Console:SecureCookies", false),
                    cors_origins_count = corsOriginsCount,
                    public_origins_count = publicOrigins.Length,
                    trusted_proxies_configured = trustedProxyCount > 0,
                    csrf_protection = true,
                    client_ip = peerIp?.ToString() ?? "",
                    behind_proxy = behindProxy,
                    // untrusted proxy means every client resolves to the proxy address
                    forwarded_ignored = behindProxy && trustedProxyCount == 0,
                    console_public = publicOrigins.Length > 0
                },
                features = new
                {
                    oidc_providers = await db.OidcProviders.CountAsync(p => p.IsEnabled)
                },
                rate_limiting = new
                {
                    store = config.GetValue<string>("RateLimiting:Store") ?? "Memory"
                },
                caching = new
                {
                    provider = config.GetValue<string>("Caching:ProviderType") ?? "Memory"
                },
                file_storage = new
                {
                    directory = config.GetValue<string>("FileStorage:StorageDirectory") ?? ""
                },
                telemetry = new
                {
                    provider = values["Telemetry:Provider"],
                    prometheus_path = telemetry.ActiveMetricsPath
                },
                mcp = new
                {
                    enabled = config.GetValue<bool>("Mcp:Enabled"),
                    path = config.GetValue<string>("Mcp:Path") ?? "/mcp",
                    require_authentication = config.GetValue<bool>("Mcp:RequireAuthentication")
                },
                chat = new
                {
                    enabled = config.GetValue<bool>("Mcp:ChatEnabled"),
                    configured = chatCfg?.IsConfigured ?? false,
                    provider = chatCfg?.Provider ?? string.Empty,
                    model = chatCfg?.Model ?? string.Empty
                },
                writable = Services.Configuration.SettingsWriteService.Schema
                .Select(w => new
                {
                    key = w.Key,
                    kind = w.Kind,
                    requires_restart = w.RequiresRestart,
                    min = w.Min,
                    max = w.Max,
                    choices = w.Choices,
                    env = writer.LockedBy(w.Key),
                    value = values.GetValueOrDefault(w.Key)
                })
            });
        }).ExcludeFromDescription();

        // applies allowlisted keys only
        app.MapPut("/_/api/settings", async (
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

            // refuse a change that would lock the caller out of the console
            // older clients send WebUi keys
            changes = changes.GroupBy(c => PortwayApi.Services.Configuration.ConsoleSectionAlias.Canonical(c.Key), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.OrdinalIgnoreCase);

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

        // config change audit trail

        // masked status, never the raw api key
        app.MapGet("/_/api/mcp/config", async (PortwayApi.Services.Mcp.McpConfigService mcpConfig) =>
        {
            var status = await mcpConfig.GetStatusAsync();
            return Results.Json(status);
        }).ExcludeFromDescription();

        // partial update, an omitted field is left unchanged
        app.MapPost("/_/api/mcp/config", async (
            HttpRequest request,
            PortwayApi.Services.Mcp.McpConfigService mcpConfig) =>
        {
            System.Text.Json.Nodes.JsonNode? body = null;
            try { body = await System.Text.Json.Nodes.JsonNode.ParseAsync(request.Body); }
            catch { return Results.BadRequest(new { error = "Invalid JSON body" }); }

            var provider = body?["provider"]?.GetValue<string>();
            var model = body?["model"]?.GetValue<string>();
            var apiKey = body?["apiKey"]?.GetValue<string>();

            if (provider is not null && string.IsNullOrWhiteSpace(provider))
                return Results.BadRequest(new { error = "provider cannot be blank" });
            if (apiKey is not null && string.IsNullOrWhiteSpace(apiKey))
                return Results.BadRequest(new { error = "apiKey cannot be blank" });

            await mcpConfig.SaveConfigAsync(provider, model, apiKey);
            Audit(request.HttpContext, "update", "mcp-config", "chat configuration",
                provider is not null ? $"provider={provider}" : null);
            return Results.Ok(new { ok = true });
        }).ExcludeFromDescription();

        // clears provider, model and key
        app.MapDelete("/_/api/mcp/config", async (
            HttpContext context,
            PortwayApi.Services.Mcp.McpConfigService mcpConfig,
            CancellationToken ct) =>
        {
            await mcpConfig.ClearConfigAsync(ct);
            Audit(context, "delete", "mcp-config", "chat configuration");
            return Results.Ok(new { ok = true });
        }).ExcludeFromDescription();

        // config change audit trail
    }

    /// <summary>
    /// Refuses a deployment change that would put the caller outside the console gate.
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

        var touchesOrigins = changes.ContainsKey("Console:PublicOrigins");
        var touchesProxies = changes.ContainsKey("ForwardedHeaders:KnownProxies")
                          || changes.ContainsKey("ForwardedHeaders:KnownNetworks");
        if (!touchesOrigins && !touchesProxies) return null;

        var origins = Strings("Console:PublicOrigins",
            config.GetSection("Console:PublicOrigins").Get<string[]>() ?? []);

        if (origins.Length > 0 && IsPublicOriginAllowed(ctx.Request, origins))
            return null;

        var clientIp = EffectiveClientIp(ctx, config, changes);
        if (clientIp is not null && urlValidator.IsClientIpAllowed(clientIp))
            return null;

        var field = touchesOrigins ? "Console:PublicOrigins" : "ForwardedHeaders:KnownProxies";
        return ($"This change would refuse your own requests to the console (seen as {clientIp?.ToString() ?? "an unknown address"}). " +
                "Keep an entry that covers you.", field);
    }

    /// <summary>
    /// Client address the console gate compares once the change applies.
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
                .Any(n => IPNetwork.TryParse(n, out var net) && net.Contains(peer));

        if (!trusted) return peer;

        // first hop only, wrong for partially trusted multi-proxy chains
        var forwarded = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (string.IsNullOrEmpty(forwarded)) return peer;

        var first = forwarded.Split(',')[0].Trim();
        return IPAddress.TryParse(first, out var client) ? client : peer;
    }
}
