namespace PortwayApi.Endpoints;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PortwayApi.Classes;
using PortwayApi.Classes.OpenApi;
using PortwayApi.Helpers;

public static partial class WebUiEndpointExtensions
{
    private static void MapInfoRoutes(WebApplication app, string appVersion)
    {
        app.MapGet("/_/api/customization", (IConfiguration config) =>
        {
            return Results.Json(new
            {
                promo_text = config.GetValue<string>("Console:Customization:PromoText"),
                promo_login = config.GetValue<bool>("Console:Customization:PromoLogin", false)
            });
        }).ExcludeFromDescription();

        app.MapGet("/_/api/overview", (IOptionsMonitor<OpenApiSettings> openApiMonitor) =>
        {
            var sqlEps = EndpointHandler.GetSqlEndpoints();
            var proxyEps = EndpointHandler.GetProxyEndpoints();
            var fileEps = EndpointHandler.GetFileEndpoints();
            var staticEps = EndpointHandler.GetStaticEndpoints();
            var webhookEps = EndpointHandler.GetSqlWebhookEndpoints();
            var compositeCount = proxyEps.Count(e => e.Value.Type.ToString() == "Composite");
            var proxyCount = proxyEps.Count(e => e.Value.Type.ToString() != "Composite");
            var envSettings = app.Services.GetRequiredService<EnvironmentSettings>();
            var uptime = (long)(DateTime.UtcNow - ProcessStartTime).TotalSeconds;
            var promoText = app.Configuration.GetValue<string>("Console:Customization:PromoText");
            var promoLogin = app.Configuration.GetValue<bool>("Console:Customization:PromoLogin", false);

            return Results.Json(new
            {
                version = appVersion,
                uptime = $"{uptime}s",
                promo_text = promoText,
                promo_login = promoLogin,
                endpoints = new
                {
                    sql = sqlEps.Count,
                    proxy = proxyCount,
                    composite = compositeCount,
                    file = fileEps.Count,
                    @static = staticEps.Count,
                    webhook = webhookEps.Count,
                    total = sqlEps.Count + proxyCount + compositeCount
                                + fileEps.Count + staticEps.Count + webhookEps.Count
                },
                environments = envSettings.AllowedEnvironments.Count,
                api_version = "v" + OpenApiEndpointCatalog.All()
                    .Where(e => e.Definition.Enabled)
                    .Select(e => EndpointVersion.Number(e.Definition.Version))
                    .DefaultIfEmpty(1)
                    .Max(),
                openapi_enabled = openApiMonitor.CurrentValue.Enabled
            });
        }).ExcludeFromDescription();

        object HealthPayload(Microsoft.Extensions.Diagnostics.HealthChecks.HealthReport report) => new
        {
            status = report.Status.ToString(),
            checking = ReferenceEquals(report, PortwayApi.Services.HealthCheckService.Checking),
            checked_at = DateTime.UtcNow,
            uptime_seconds = (long)(DateTime.UtcNow - ProcessStartTime).TotalSeconds,
            version = appVersion,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration_ms = Math.Round(e.Value.Duration.TotalMilliseconds, 1),
                items = HealthItems(e.Key, e.Value).Select(i => new { name = i.Name, status = i.Status, problem = i.Problem, href = i.Href })
            })
        };

        app.MapGet("/_/api/health/checks", async (PortwayApi.Services.HealthCheckService healthService, CancellationToken ct) =>
            Results.Json(HealthPayload(await healthService.CheckHealthAsync(ct))))
            .ExcludeFromDescription();

        // outbound probes run at most once per interval however often the button is pressed
        app.MapPost("/_/api/health/checks/refresh", async (PortwayApi.Services.HealthCheckService healthService, CancellationToken ct) =>
            Results.Json(HealthPayload(await healthService.RefreshAsync(TimeSpan.FromSeconds(10), ct))))
            .ExcludeFromDescription();

        app.MapGet("/_/api/endpoints", () =>
        {
            var sqlEps = EndpointHandler.GetSqlEndpoints();
            var proxyEps = EndpointHandler.GetProxyEndpoints();
            var fileEps = EndpointHandler.GetFileEndpoints();
            var staticEps = EndpointHandler.GetStaticEndpoints();
            var webhookEps = EndpointHandler.GetSqlWebhookEndpoints();

            return Results.Json(new
            {
                sql = sqlEps.Select(e => new
                {
                    name = ConsoleName(e.Key, e.Value),
                    version = e.Value.Version,
                    scope = e.Key,
                    methods = e.Value.Methods,
                    hidden = e.Value.Hidden,
                    enabled = e.Value.Enabled,
                    deprecated = e.Value.Deprecated,
                    sunset = e.Value.Sunset,
                    is_mcp_exposed = e.Value.IsMcpExposed,
                    tenancy = e.Value.Tenancy?.Keys,
                    @namespace = e.Value.Namespace,
                    schema = e.Value.DatabaseSchema,
                    object_name = e.Value.DatabaseObjectName,
                    object_type = e.Value.DatabaseObjectType ?? "Table"
                }).OrderBy(e => e.name),
                proxy = proxyEps.Where(e => e.Value.Type.ToString() != "Composite").Select(e => new
                {
                    name = ConsoleName(e.Key, e.Value),
                    version = e.Value.Version,
                    scope = e.Key,
                    url = UrlRedaction.Redact(e.Value.Url),
                    methods = e.Value.Methods,
                    hidden = e.Value.Hidden,
                    enabled = e.Value.Enabled,
                    deprecated = e.Value.Deprecated,
                    sunset = e.Value.Sunset,
                    is_mcp_exposed = e.Value.IsMcpExposed,
                    tenancy = e.Value.Tenancy?.Keys,
                    @namespace = e.Value.Namespace
                }).OrderBy(e => e.name),
                composite = proxyEps.Where(e => e.Value.Type.ToString() == "Composite").Select(e => new
                {
                    name = ConsoleName(e.Key, e.Value),
                    version = e.Value.Version,
                    scope = e.Key,
                    url = UrlRedaction.Redact(e.Value.Url),
                    methods = e.Value.Methods,
                    hidden = e.Value.Hidden,
                    enabled = e.Value.Enabled,
                    deprecated = e.Value.Deprecated,
                    sunset = e.Value.Sunset,
                    is_mcp_exposed = e.Value.IsMcpExposed,
                    @namespace = e.Value.Namespace
                }).OrderBy(e => e.name),
                file = fileEps.Select(e => new
                {
                    name = ConsoleName(e.Key, e.Value),
                    version = e.Value.Version,
                    scope = "files/" + e.Key,
                    methods = e.Value.Methods,
                    hidden = e.Value.Hidden,
                    enabled = e.Value.Enabled,
                    deprecated = e.Value.Deprecated,
                    sunset = e.Value.Sunset,
                    is_mcp_exposed = e.Value.IsMcpExposed,
                    tenancy = e.Value.Tenancy?.Keys,
                    @namespace = e.Value.Namespace
                }).OrderBy(e => e.name),
                @static = staticEps.Select(e => new
                {
                    name = ConsoleName(e.Key, e.Value),
                    version = e.Value.Version,
                    scope = e.Key,
                    methods = e.Value.Methods,
                    hidden = e.Value.Hidden,
                    enabled = e.Value.Enabled,
                    deprecated = e.Value.Deprecated,
                    sunset = e.Value.Sunset,
                    is_mcp_exposed = e.Value.IsMcpExposed,
                    @namespace = e.Value.Namespace
                }).OrderBy(e => e.name),
                webhook = webhookEps.Select(e => new
                {
                    name = ConsoleName(e.Key, e.Value),
                    version = e.Value.Version,
                    scope = e.Key,
                    methods = e.Value.Methods,
                    hidden = e.Value.Hidden,
                    enabled = e.Value.Enabled,
                    deprecated = e.Value.Deprecated,
                    sunset = e.Value.Sunset,
                    is_mcp_exposed = e.Value.IsMcpExposed,
                    @namespace = e.Value.Namespace
                }).OrderBy(e => e.name)
            });
        }).ExcludeFromDescription();

    }

    // one entry per environment or endpoint a check probed, with a short problem in place of the raw error
    internal static IEnumerable<ConsoleHealthItem> HealthItems(string check, Microsoft.Extensions.Diagnostics.HealthChecks.HealthReportEntry entry) =>
        check is "SqlConnectivity" or "ProxyEndpoints"
            ? entry.Data.Select(d => HealthItem(check, d.Key, System.Text.Json.JsonSerializer.SerializeToElement(d.Value)))
            : [];

    private static ConsoleHealthItem HealthItem(string check, string name, System.Text.Json.JsonElement data)
    {
        var status = data.TryGetProperty("Status", out var s) ? s.GetString() ?? "Unknown" : "Unknown";
        var error = data.TryGetProperty("Error", out var e) ? e.GetString() ?? "" : "";
        var problem = status switch
        {
            "Healthy" => null,
            "NotConfigured" => "Not configured",
            _ when data.TryGetProperty("StatusCode", out var code) => $"HTTP {code.GetInt32()}",
            _ when error.Contains("timed out", StringComparison.OrdinalIgnoreCase) => "Timed out",
            _ when check == "SqlConnectivity" => "Connection failed",
            _ => "Unreachable"
        };
        var href = check switch
        {
            "SqlConnectivity" => $"environments#{name}",
            "ProxyEndpoints" when EndpointHandler.GetProxyEndpoints().TryGetValue(name, out var definition) =>
                $"endpoints#{(definition.Type.ToString() == "Composite" ? "composite" : "proxy")}/{ConsoleName(name, definition)}",
            _ => null
        };
        return new ConsoleHealthItem(name, status, problem, href);
    }

    // path of the folder the endpoint loaded from, below its endpoint type folder
    private static string ConsoleName(string key, EndpointDefinition definition)
    {
        if (definition.ConfigDirectory is not { } directory)
            return EndpointVersion.FolderPath(key);

        var relative = Path.GetRelativePath(Path.Combine(Directory.GetCurrentDirectory(), "endpoints"), directory).Replace('\\', '/');
        return relative[(relative.IndexOf('/') + 1)..];
    }
}

internal sealed record ConsoleHealthItem(string Name, string Status, string? Problem, string? Href);
