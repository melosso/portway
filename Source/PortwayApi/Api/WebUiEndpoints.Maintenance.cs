namespace PortwayApi.Endpoints;

using PortwayApi.Auth;
using PortwayApi.Services.Database;

public static partial class WebUiEndpointExtensions
{
    private static void MapMaintenanceRoutes(WebApplication app, PortwayApi.Services.Configuration.ConfigAuditService configAudit)
    {
        app.MapPost("/ui/api/maintenance/run", async (DatabaseMaintenanceService maintenance, CancellationToken ct) =>
        {
            var results = await maintenance.RunOnceAsync(ct);
            return Results.Json(new { results = results.Select(MaintenanceResultDto) });
        }).ExcludeFromDescription();

        app.MapGet("/ui/api/backups", () => Results.Json(new
        {
            backups = BackupArchive.List(Directory.GetCurrentDirectory())
                .Select(b => new { name = b.Name, bytes = b.Bytes, created_utc = b.CreatedUtc }),
            pending_restore = BackupArchive.PendingRestore(Directory.GetCurrentDirectory())
        })).ExcludeFromDescription();

        app.MapPost("/ui/api/backups/{name}/restore", (string name, HttpContext ctx) =>
        {
            try
            {
                BackupArchive.StageRestore(Directory.GetCurrentDirectory(), name);
            }
            catch (FileNotFoundException)
            {
                return Results.Json(new { error = "Backup not found" }, statusCode: 404);
            }
            catch (InvalidDataException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 400);
            }

            configAudit.Record("restore", "backup", name, ctx.Connection.RemoteIpAddress?.ToString(), "applied at the next start");
            return Results.Json(new { pending_restore = name, restart_required = true });
        }).ExcludeFromDescription();

        app.MapDelete("/ui/api/backups/restore", (HttpContext ctx) =>
        {
            BackupArchive.CancelRestore(Directory.GetCurrentDirectory());
            configAudit.Record("cancel restore", "backup", "restore-pending", ctx.Connection.RemoteIpAddress?.ToString());
            return Results.Json(new { ok = true });
        }).ExcludeFromDescription();

        app.MapPost("/ui/api/backups", async (DatabaseMaintenanceService maintenance, CancellationToken ct) =>
            Results.Json(new { created = await maintenance.CreateBackupAsync(ct) }))
            .ExcludeFromDescription();

        // archives contain password and token hashes
        app.MapGet("/ui/api/backups/{name}", async (string name, HttpContext ctx, AdminUserService users) =>
        {
            if (ctx.Items[SignedInUserKey] is not int me || await users.FindByIdAsync(me) is not { Role: AdminUserRoles.Administrator })
                return Results.Json(new { error = "This action needs an administrator account" }, statusCode: 403);

            if (BackupArchive.Resolve(Directory.GetCurrentDirectory(), name) is not { } path)
                return Results.Json(new { error = "Backup not found" }, statusCode: 404);

            configAudit.Record("download", "backup", name, ctx.Connection.RemoteIpAddress?.ToString());
            return Results.File(path, "application/zip", name);
        }).ExcludeFromDescription();
    }

    private static object MaintenanceResultDto(DatabaseMaintenanceResult r) => new
    {
        database = r.Database,
        integrity = r.Integrity,
        vacuumed = r.Vacuumed,
        reclaimed_bytes = r.BytesBefore - r.BytesAfter,
        skip_reason = r.SkipReason
    };
}
