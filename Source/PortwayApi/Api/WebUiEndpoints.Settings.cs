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
    /// Current values; secrets report presence.
    /// </summary>
    internal static Dictionary<string, object?> SettingValues(IConfiguration config, PortwayApi.Classes.OpenApi.OpenApiSettings openApi, string effectiveTelemetryProvider)
    {
        string[] List(string key) => config.GetSection(key).Get<string[]>() ?? [];
        Dictionary<string, string?> Map(string key) => config.GetSection(key).GetChildren().ToDictionary(c => c.Key, c => c.Value);
        bool IsSet(string key) => !string.IsNullOrEmpty(config[key]);
        string Text(string key, string fallback = "") => config[key] ?? fallback;

        return new(StringComparer.OrdinalIgnoreCase)
        {
            ["RateLimiting:Enabled"] = config.GetValue<bool>("RateLimiting:Enabled"),
            ["RateLimiting:IpLimit"] = config.GetValue<int>("RateLimiting:IpLimit"),
            ["RateLimiting:IpWindowSeconds"] = config.GetValue("RateLimiting:IpWindowSeconds", 60),
            ["RateLimiting:TokenLimit"] = config.GetValue<int>("RateLimiting:TokenLimit"),
            ["RateLimiting:TokenWindowSeconds"] = config.GetValue("RateLimiting:TokenWindowSeconds", 60),
            ["RateLimiting:Provider"] = Text("RateLimiting:Provider", "Memory"),
            ["RateLimiting:Redis:ConnectionString"] = IsSet("RateLimiting:Redis:ConnectionString"),
            ["Caching:Enabled"] = config.GetValue<bool>("Caching:Enabled"),
            ["Caching:DefaultCacheDurationSeconds"] = config.GetValue<int>("Caching:DefaultCacheDurationSeconds"),
            ["Caching:MemoryCacheSizeLimitMB"] = config.GetValue<int>("Caching:MemoryCacheSizeLimitMB"),
            ["Caching:Provider"] = Text("Caching:Provider", "Memory"),
            ["Caching:EndpointCacheDurations"] = Map("Caching:EndpointCacheDurations"),
            ["Caching:CacheableContentTypes"] = List("Caching:CacheableContentTypes"),
            ["Caching:Redis:ConnectionString"] = IsSet("Caching:Redis:ConnectionString"),
            ["Caching:Redis:InstanceName"] = Text("Caching:Redis:InstanceName", "Portway:"),
            ["Caching:Redis:Database"] = config.GetValue("Caching:Redis:Database", 0),
            ["Caching:Redis:UseSsl"] = config.GetValue("Caching:Redis:UseSsl", false),
            ["Caching:Redis:ConnectTimeoutMs"] = config.GetValue("Caching:Redis:ConnectTimeoutMs", 5000),
            ["Caching:Redis:AbortOnConnectFail"] = config.GetValue("Caching:Redis:AbortOnConnectFail", false),
            ["Caching:Redis:FallbackToMemoryCache"] = config.GetValue("Caching:Redis:FallbackToMemoryCache", true),
            ["Caching:Redis:MaxRetryAttempts"] = config.GetValue("Caching:Redis:MaxRetryAttempts", 3),
            ["Caching:Redis:RetryDelayMs"] = config.GetValue("Caching:Redis:RetryDelayMs", 200),
            ["SqlConnectionPooling:Enabled"] = config.GetValue<bool>("SqlConnectionPooling:Enabled"),
            ["SqlConnectionPooling:MinPoolSize"] = config.GetValue<int>("SqlConnectionPooling:MinPoolSize"),
            ["SqlConnectionPooling:MaxPoolSize"] = config.GetValue<int>("SqlConnectionPooling:MaxPoolSize"),
            ["SqlConnectionPooling:ConnectionTimeoutSeconds"] = config.GetValue("SqlConnectionPooling:ConnectionTimeoutSeconds", 15),
            ["SqlConnectionPooling:CommandTimeoutSeconds"] = config.GetValue("SqlConnectionPooling:CommandTimeoutSeconds", 30),
            ["SqlConnectionPooling:ApplicationName"] = Text("SqlConnectionPooling:ApplicationName", "PortwayAPI"),
            ["EndpointReloading:Enabled"] = config.GetValue<bool>("EndpointReloading:Enabled"),
            ["EndpointReloading:DebounceMs"] = config.GetValue<int>("EndpointReloading:DebounceMs"),
            ["Serilog:MinimumLevel:Default"] = Text("Serilog:MinimumLevel:Default", "Information"),
            ["Serilog:MinimumLevel:Override"] = Map("Serilog:MinimumLevel:Override"),
            ["RequestTrafficLogging:Enabled"] = config.GetValue("RequestTrafficLogging:Enabled", false),
            ["RequestTrafficLogging:Provider"] = Text("RequestTrafficLogging:Provider", "File"),
            ["RequestTrafficLogging:SqlitePath"] = Text("RequestTrafficLogging:SqlitePath", "log/traffic_logs.db"),
            ["RequestTrafficLogging:LogDirectory"] = Text("RequestTrafficLogging:LogDirectory", "log/traffic"),
            ["RequestTrafficLogging:FilePrefix"] = Text("RequestTrafficLogging:FilePrefix", "proxy_traffic_"),
            ["RequestTrafficLogging:MaxFileSizeMB"] = config.GetValue("RequestTrafficLogging:MaxFileSizeMB", 50),
            ["RequestTrafficLogging:MaxFileCount"] = config.GetValue("RequestTrafficLogging:MaxFileCount", 10),
            ["RequestTrafficLogging:QueueCapacity"] = config.GetValue("RequestTrafficLogging:QueueCapacity", 10000),
            ["RequestTrafficLogging:BatchSize"] = config.GetValue("RequestTrafficLogging:BatchSize", 100),
            ["RequestTrafficLogging:FlushIntervalMs"] = config.GetValue("RequestTrafficLogging:FlushIntervalMs", 1000),
            ["RequestTrafficLogging:CaptureHeaders"] = config.GetValue("RequestTrafficLogging:CaptureHeaders", true),
            ["RequestTrafficLogging:CaptureRequestBodies"] = config.GetValue("RequestTrafficLogging:CaptureRequestBodies", false),
            ["RequestTrafficLogging:CaptureResponseBodies"] = config.GetValue("RequestTrafficLogging:CaptureResponseBodies", false),
            ["RequestTrafficLogging:MaxBodyCaptureSizeBytes"] = config.GetValue("RequestTrafficLogging:MaxBodyCaptureSizeBytes", 4096),
            ["RequestTrafficLogging:InfoLoggingEnabled"] = config.GetValue("RequestTrafficLogging:InfoLoggingEnabled", true),
            ["DatabaseMaintenance:Enabled"] = config.GetValue("DatabaseMaintenance:Enabled", true),
            ["DatabaseMaintenance:Schedule"] = Text("DatabaseMaintenance:Schedule", "03:00"),
            ["DatabaseMaintenance:RunOnStartup"] = config.GetValue("DatabaseMaintenance:RunOnStartup", false),
            ["DatabaseMaintenance:FreePageRatioThreshold"] = config.GetValue("DatabaseMaintenance:FreePageRatioThreshold", 0.25),
            ["DatabaseMaintenance:Backup:Enabled"] = config.GetValue("DatabaseMaintenance:Backup:Enabled", true),
            ["DatabaseMaintenance:Backup:Retention"] = config.GetValue("DatabaseMaintenance:Backup:Retention", 7),
            ["DatabaseMaintenance:AuditRetention:Days"] = config.GetValue("DatabaseMaintenance:AuditRetention:Days", 180),
            ["DatabaseMaintenance:AuditRetention:MaxSizeMb"] = config.GetValue("DatabaseMaintenance:AuditRetention:MaxSizeMb", 0),
            ["Mcp:Enabled"] = config.GetValue<bool>("Mcp:Enabled"),
            ["Mcp:Path"] = Text("Mcp:Path", "/mcp"),
            ["Mcp:ChatEnabled"] = config.GetValue<bool>("Mcp:ChatEnabled"),
            ["Mcp:ToolTimeoutSeconds"] = config.GetValue("Mcp:ToolTimeoutSeconds", 30),
            ["Mcp:DefaultPageSize"] = config.GetValue("Mcp:DefaultPageSize", 50),
            ["Mcp:MaxPageSize"] = config.GetValue("Mcp:MaxPageSize", 200),
            ["Mcp:MaxToolResultChars"] = config.GetValue("Mcp:MaxToolResultChars", 12000),
            ["Console:SecureCookies"] = config.GetValue("Console:SecureCookies", false),
            ["Console:Enabled"] = config.GetValue("Console:Enabled", true),
            ["Console:Customization:LandingPageEnabled"] = config.GetValue("Console:Customization:LandingPageEnabled", true),
            ["Console:Customization:PromoText"] = Text("Console:Customization:PromoText"),
            ["Console:Customization:PromoLogin"] = config.GetValue("Console:Customization:PromoLogin", false),
            ["Console:Customization:LoginFooter"] = Text("Console:Customization:LoginFooter"),
            ["Console:CorsOrigins"] = List("Console:CorsOrigins"),
            ["Console:PublicOrigins"] = List("Console:PublicOrigins"),
            ["Console:SeedPassword"] = IsSet("Console:SeedPassword"),
            ["Console:SeedToken"] = IsSet("Console:SeedToken"),
            ["ForwardedHeaders:KnownProxies"] = List("ForwardedHeaders:KnownProxies"),
            ["ForwardedHeaders:KnownNetworks"] = List("ForwardedHeaders:KnownNetworks"),
            ["AllowedHosts"] = Text("AllowedHosts", "*").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            ["PathBase"] = Text("PathBase"),
            ["Proxy:Username"] = Text("Proxy:Username"),
            ["Proxy:Password"] = IsSet("Proxy:Password"),
            ["Proxy:Domain"] = Text("Proxy:Domain"),
            ["Oidc:Enabled"] = config.GetValue("Oidc:Enabled", true),
            ["FileStorage:StorageDirectory"] = Text("FileStorage:StorageDirectory", "files"),
            ["FileStorage:MaxFileSizeBytes"] = config.GetValue<long>("FileStorage:MaxFileSizeBytes"),
            ["FileStorage:UseMemoryCache"] = config.GetValue("FileStorage:UseMemoryCache", true),
            ["FileStorage:MemoryCacheDurationSeconds"] = config.GetValue("FileStorage:MemoryCacheDurationSeconds", 60),
            ["FileStorage:MaxTotalMemoryCacheMB"] = config.GetValue("FileStorage:MaxTotalMemoryCacheMB", 200),
            ["FileStorage:AllowedExtensions"] = List("FileStorage:AllowedExtensions"),
            ["FileStorage:BlockedExtensions"] = List("FileStorage:BlockedExtensions"),
            ["Telemetry:Provider"] = config["Telemetry:Provider"] is { Length: > 0 } provider ? provider : effectiveTelemetryProvider,
            ["Telemetry:ServiceName"] = Text("Telemetry:ServiceName"),
            ["Telemetry:Otlp:Endpoint"] = Text("Telemetry:Otlp:Endpoint"),
            ["Telemetry:ResourceAttributes"] = Text("Telemetry:ResourceAttributes"),
            ["Telemetry:Prometheus:Path"] = Text("Telemetry:Prometheus:Path", "/metrics"),
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
            ["OpenApi:Scalar:Theme"] = openApi.Scalar.Theme,
            ["OpenApi:Scalar:Layout"] = openApi.Scalar.Layout,
            ["OpenApi:Scalar:ShowSidebar"] = openApi.Scalar.ShowSidebar,
            ["OpenApi:Scalar:HideDownloadButton"] = openApi.Scalar.HideDownloadButton,
            ["OpenApi:Scalar:HideModels"] = openApi.Scalar.HideModels,
            ["OpenApi:Scalar:HideClientButton"] = openApi.Scalar.HideClientButton,
            ["OpenApi:Scalar:HideTestRequestButton"] = openApi.Scalar.HideTestRequestButton
        };
    }

    /// <summary>
    /// Reports the proxy account and collector address as presence only.
    /// </summary>
    internal static void HideFromViewer(Dictionary<string, object?> values)
    {
        foreach (var key in (string[])["Proxy:Username", "Proxy:Domain", "Telemetry:Otlp:Endpoint"])
            if (values[key] is string { Length: > 0 })
                values[key] = "Set";
    }

    /// <summary>
    /// Running traffic logging state.
    /// </summary>
    internal static object TrafficLoggingStatus(PortwayApi.Middleware.RequestTrafficLoggingOptions running, IConfiguration config)
    {
        var saved = config.GetSection("RequestTrafficLogging").Get<PortwayApi.Middleware.RequestTrafficLoggingOptions>() ?? new();
        return new
        {
            running = running.Enabled,
            request_bodies = running.Enabled && running.CaptureRequestBodies,
            response_bodies = running.Enabled && running.CaptureResponseBodies,
            restart_required = running != saved
        };
    }

    private static void MapSettingsRoutes(WebApplication app, PortwayApi.Services.Configuration.ConfigAuditService configAudit)
    {
        void Audit(HttpContext ctx, string action, string targetType, string target, string? details = null, string? backupPath = null)
            => configAudit.Record(action, targetType, target, ctx.Connection.RemoteIpAddress?.ToString(), details, backupPath);

        app.MapGet("/_/api/settings", async (IConfiguration config, PortwayApi.Services.Configuration.SettingsWriteService writer, PortwayApi.Services.Telemetry.TelemetryOptions telemetry, PortwayApi.Services.Mcp.McpConfigService? mcpConfig, PortwayApi.Services.Database.DatabaseMaintenanceService? dbMaintenance, PortwayApi.Auth.AdminUserService users, PortwayApi.Auth.AuthDbContext db, IOptionsMonitor<PortwayApi.Classes.OpenApi.OpenApiSettings> openApiMonitor, IOptions<PortwayApi.Middleware.RequestTrafficLoggingOptions> trafficAtStartup, HttpContext ctx) =>
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
            if (ctx.Items[SignedInRoleKey] as string != PortwayApi.Auth.AdminUserRoles.Administrator)
                HideFromViewer(values);

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
                    store = config.GetValue<string>("RateLimiting:Provider") ?? "Memory"
                },
                caching = new
                {
                    provider = config.GetValue<string>("Caching:Provider") ?? "Memory"
                },
                traffic_logging = TrafficLoggingStatus(trafficAtStartup.Value, config),
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
                    path = config.GetValue<string>("Mcp:Path") ?? "/mcp"
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
                    kind = w.Kind.ToString().ToLowerInvariant(),
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
            if (WouldLockCallerOut(ctx, config, urlValidator, changes) is { } lockout)
                return Results.Json(new { error = lockout.Message, field = lockout.Field }, statusCode: 400);

            var result = await writer.ApplyAsync(changes, ctx.RequestAborted);
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
        if (changes.TryGetValue("AllowedHosts", out var hostsRaw) && hostsRaw.ValueKind == JsonValueKind.Array)
        {
            var host = ctx.Request.Host.Host;
            var hosts = PendingList(changes, "AllowedHosts", []);
            if (hosts.Length > 0 && !hosts.Any(h => h == "*" ||
                    h.Equals(host, StringComparison.OrdinalIgnoreCase) ||
                    h.Trim('[', ']').Equals(host.Trim('[', ']'), StringComparison.OrdinalIgnoreCase) ||
                    h.StartsWith("*.") && host.EndsWith(h[1..], StringComparison.OrdinalIgnoreCase)))
                return ($"This change would refuse requests for {host}, the host this console is opened on. Keep an entry that covers it.", "AllowedHosts");
        }

        var touchesOrigins = changes.ContainsKey("Console:PublicOrigins");
        var touchesProxies = changes.ContainsKey("ForwardedHeaders:KnownProxies")
                          || changes.ContainsKey("ForwardedHeaders:KnownNetworks");
        if (!touchesOrigins && !touchesProxies) return null;

        var origins = PendingList(changes, "Console:PublicOrigins",
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
    /// List value once the change applies.
    /// </summary>
    private static string[] PendingList(IDictionary<string, JsonElement> changes, string key, string[] current) =>
        changes.TryGetValue(key, out var raw) && raw.ValueKind == JsonValueKind.Array
            ? raw.EnumerateArray()
                 .Where(e => e.ValueKind == JsonValueKind.String)
                 .Select(e => (e.GetString() ?? "").Trim())
                 .Where(v => v.Length > 0)
                 .ToArray()
            : current;

    /// <summary>
    /// Client address the console gate compares once the change applies.
    /// </summary>
    private static IPAddress? EffectiveClientIp(
        HttpContext ctx, IConfiguration config, IDictionary<string, JsonElement> changes)
    {
        var peer = ctx.Connection.RemoteIpAddress;
        if (peer is null) return null;
        if (peer.IsIPv4MappedToIPv6) peer = peer.MapToIPv4();

        string[] Trusted(string key) => PendingList(changes, key, config.GetSection(key).Get<string[]>() ?? []);

        var trusted = Trusted("ForwardedHeaders:KnownProxies")
            .Any(p => IPAddress.TryParse(p, out var ip) && ip.Equals(peer))
            || Trusted("ForwardedHeaders:KnownNetworks")
                .Any(n => IPNetwork.TryParse(n, out var net) && net.Contains(peer));

        if (!trusted) return peer;

        // first hop only, wrong for partially trusted multi-proxy chains
        var forwarded = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (string.IsNullOrEmpty(forwarded)) return peer;

        var first = forwarded.Split(',')[0].Trim();
        return IPAddress.TryParse(first, out var client) ? client : peer;
    }
}
