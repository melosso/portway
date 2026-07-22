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
    private static void MapAuditRoutes(WebApplication app, PortwayApi.Services.Configuration.ConfigAuditService configAudit)
    {
        void Audit(HttpContext ctx, string action, string targetType, string target, string? details = null, string? backupPath = null)
            => configAudit.Record(action, targetType, target, ctx.Connection.RemoteIpAddress?.ToString(), details, backupPath);

        app.MapGet("/ui/api/audit", (HttpRequest request) =>
        {
            var limit = int.TryParse(request.Query["limit"], out var l) ? l : 50;
            var entries = configAudit.GetRecent(limit).Select(e => new
            {
                id = e.Id, timestamp = e.Timestamp, client_ip = e.ClientIp,
                action = e.Action, target_type = e.TargetType, target = e.Target,
                details = e.Details, restorable = e.BackupPath != null && File.Exists(e.BackupPath)
            });
            return Results.Json(new { entries });
        }).ExcludeFromDescription();

        // Restores the pre-change backup recorded on an audit entry
        app.MapPost("/ui/api/audit/{id:long}/restore", (long id, HttpContext context) =>
        {
            var entry = configAudit.GetById(id);
            if (entry?.BackupPath is null || !File.Exists(entry.BackupPath))
                return Results.Json(new { error = "No backup available for this change" }, statusCode: 404);

            string? targetPath = entry.TargetType switch
            {
                "environment-settings" => Path.Combine(Directory.GetCurrentDirectory(), "environments", "settings.json"),
                "environment" => Path.Combine(Directory.GetCurrentDirectory(), "environments", entry.Target, "settings.json"),
                "endpoint" => entry.Target.IndexOf('/') is var slash && slash > 0
                    ? ResolveEndpointPath(entry.Target[..slash], entry.Target[(slash + 1)..]).Item1
                    : null,
                _ => null
            };
            if (targetPath is null)
                return Results.Json(new { error = "This change type cannot be restored" }, statusCode: 400);

            if (!PortwayApi.Services.Configuration.ConfigBackupService.Restore(entry.BackupPath, targetPath))
                return Results.Json(new { error = "Restore failed" }, statusCode: 500);

            // Reload affected config
            if (entry.TargetType == "endpoint" && entry.Target.IndexOf('/') is var s && s > 0)
            {
                var epType = TypeStringToEndpointType(entry.Target[..s]);
                if (epType.HasValue) EndpointHandler.ReloadEndpointType(epType.Value);
            }
            else
            {
                app.Services.GetRequiredService<EnvironmentSettings>().Reload();
            }

            Audit(context, "restore", entry.TargetType, entry.Target, $"restored audit entry {id}");
            return Results.Ok(new { ok = true });
        }).ExcludeFromDescription();

        // Token management endpoints
    }
}
