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
    private static void MapInfoRoutes(WebApplication app, string appVersion)
    {
        app.MapGet("/ui/api/customization", (IConfiguration config) =>
        {
            return Results.Json(new
            {
                promo_text = config.GetValue<string>("WebUi:Customization:PromoText"),
                promo_login = config.GetValue<bool>("WebUi:Customization:PromoLogin", false)
            });
        }).ExcludeFromDescription();

        app.MapGet("/ui/api/overview", (IOptionsMonitor<OpenApiSettings> openApiMonitor) =>
        {
            var sqlEps        = EndpointHandler.GetSqlEndpoints();
            var proxyEps      = EndpointHandler.GetProxyEndpoints();
            var fileEps       = EndpointHandler.GetFileEndpoints();
            var staticEps     = EndpointHandler.GetStaticEndpoints();
            var webhookEps    = EndpointHandler.GetSqlWebhookEndpoints();
            var compositeCount = proxyEps.Count(e => e.Value.Type.ToString() == "Composite");
            var proxyCount     = proxyEps.Count(e => e.Value.Type.ToString() != "Composite");
            var envSettings   = app.Services.GetRequiredService<EnvironmentSettings>();
            var uptime        = (long)(DateTime.UtcNow - ProcessStartTime).TotalSeconds;
            var promoText     = app.Configuration.GetValue<string>("WebUi:Customization:PromoText");
            var promoLogin    = app.Configuration.GetValue<bool>("WebUi:Customization:PromoLogin", false);

            return Results.Json(new
            {
                version = appVersion,
                uptime  = $"{uptime}s",
                promo_text = promoText,
                promo_login = promoLogin,
                endpoints = new
                {
                    sql       = sqlEps.Count,
                    proxy     = proxyCount,
                    composite = compositeCount,
                    file      = fileEps.Count,
                    @static   = staticEps.Count,
                    webhook   = webhookEps.Count,
                    total     = sqlEps.Count + proxyCount + compositeCount
                                + fileEps.Count + staticEps.Count + webhookEps.Count
                },
                environments    = envSettings.AllowedEnvironments.Count,
                openapi_enabled = openApiMonitor.CurrentValue.Enabled
            });
        }).ExcludeFromDescription();

        app.MapGet("/ui/api/endpoints", () =>
        {
            var sqlEps     = EndpointHandler.GetSqlEndpoints();
            var proxyEps   = EndpointHandler.GetProxyEndpoints();
            var fileEps    = EndpointHandler.GetFileEndpoints();
            var staticEps  = EndpointHandler.GetStaticEndpoints();
            var webhookEps = EndpointHandler.GetSqlWebhookEndpoints();

            return Results.Json(new
            {
                sql = sqlEps.Select(e => new
                {
                    name           = e.Key,
                    methods        = e.Value.Methods,
                    is_private     = e.Value.IsPrivate,
                    is_mcp_exposed = e.Value.IsMcpExposed,
                    @namespace     = e.Value.Namespace,
                    schema         = e.Value.DatabaseSchema,
                    object_name    = e.Value.DatabaseObjectName,
                    object_type    = e.Value.DatabaseObjectType ?? "Table"
                }).OrderBy(e => e.name),
                proxy = proxyEps.Where(e => e.Value.Type.ToString() != "Composite").Select(e => new
                {
                    name           = e.Key,
                    url            = e.Value.Url,
                    methods        = e.Value.Methods,
                    is_private     = e.Value.IsPrivate,
                    is_mcp_exposed = e.Value.IsMcpExposed,
                    @namespace     = e.Value.Namespace
                }).OrderBy(e => e.name),
                composite = proxyEps.Where(e => e.Value.Type.ToString() == "Composite").Select(e => new
                {
                    name           = e.Key,
                    url            = e.Value.Url,
                    methods        = e.Value.Methods,
                    is_private     = e.Value.IsPrivate,
                    is_mcp_exposed = e.Value.IsMcpExposed,
                    @namespace     = e.Value.Namespace
                }).OrderBy(e => e.name),
                file = fileEps.Select(e => new
                {
                    name           = e.Key,
                    methods        = e.Value.Methods,
                    is_private     = e.Value.IsPrivate,
                    is_mcp_exposed = e.Value.IsMcpExposed,
                    @namespace     = e.Value.Namespace
                }).OrderBy(e => e.name),
                @static = staticEps.Select(e => new
                {
                    name           = e.Key,
                    methods        = e.Value.Methods,
                    is_private     = e.Value.IsPrivate,
                    is_mcp_exposed = e.Value.IsMcpExposed,
                    @namespace     = e.Value.Namespace
                }).OrderBy(e => e.name),
                webhook = webhookEps.Select(e => new
                {
                    name           = e.Key,
                    methods        = e.Value.Methods,
                    is_private     = e.Value.IsPrivate,
                    is_mcp_exposed = e.Value.IsMcpExposed,
                    @namespace     = e.Value.Namespace
                }).OrderBy(e => e.name)
            });
        }).ExcludeFromDescription();

    }
}
