using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using PortwayApi.Services.Configuration;
using Serilog;

namespace PortwayApi.Services.Database;

/// <summary>Nightly SQLite self-tuning: ANALYZE refreshes index statistics, VACUUM reclaims space when a database is bloated</summary>
public class DatabaseMaintenanceService : BackgroundService
{
    private readonly IOptionsMonitor<DatabaseMaintenanceOptions> _options;
    private readonly IConfiguration _configuration;
    private readonly ConfigAuditService _audit;

    /// <summary>Results of the most recent run; empty until the first run completes</summary>
    public IReadOnlyList<DatabaseMaintenanceResult> LastRunResults { get; private set; } = [];
    public DateTime? LastRunUtc { get; private set; }

    public DatabaseMaintenanceService(
        IOptionsMonitor<DatabaseMaintenanceOptions> options,
        IConfiguration configuration,
        ConfigAuditService audit)
    {
        _options = options;
        _configuration = configuration;
        _audit = audit;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.CurrentValue.RunOnStartup)
        {
            // Small delay so startup IO (EnsureCreated, token bootstrap) settles first
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            await RunOnceAsync(stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = DelayUntilNextRun(_options.CurrentValue.Schedule);
            await Task.Delay(delay, stoppingToken);

            if (_options.CurrentValue.Enabled)
                await RunOnceAsync(stoppingToken);
        }
    }

    /// <summary>Time until the next occurrence of the configured local time of day; falls back to 03:00 on bad input</summary>
    internal static TimeSpan DelayUntilNextRun(string schedule, DateTime? nowLocal = null)
    {
        if (!TimeSpan.TryParse(schedule, out var timeOfDay) || timeOfDay < TimeSpan.Zero || timeOfDay >= TimeSpan.FromDays(1))
            timeOfDay = TimeSpan.FromHours(3);

        var now = nowLocal ?? DateTime.Now;
        var next = now.Date.Add(timeOfDay);
        if (next <= now) next = next.AddDays(1);
        return next - now;
    }

    /// <summary>Runs one maintenance pass over all Portway SQLite databases that exist on disk</summary>
    public async Task<List<DatabaseMaintenanceResult>> RunOnceAsync(CancellationToken ct = default)
    {
        var results = new List<DatabaseMaintenanceResult>();

        foreach (var dbPath in ResolveDatabasePaths())
        {
            if (!File.Exists(dbPath)) continue;
            ct.ThrowIfCancellationRequested();

            var name = Path.GetFileName(dbPath);
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            var sizeBefore = new FileInfo(dbPath).Length;
            bool analyzed = false, vacuumed = false;
            string? skipReason = null;

            try
            {
                await using var conn = new SqliteConnection($"Data Source={dbPath}");
                await conn.OpenAsync(ct);
                await ExecAsync(conn, "PRAGMA busy_timeout = 5000", ct);

                // Flush the WAL so page counts reflect the main database file
                await ExecAsync(conn, "PRAGMA wal_checkpoint(TRUNCATE)", ct);

                await ExecAsync(conn, "ANALYZE", ct);
                analyzed = true;

                var freelist = await ScalarAsync(conn, "PRAGMA freelist_count", ct);
                var pages    = await ScalarAsync(conn, "PRAGMA page_count", ct);
                var ratio    = pages > 0 ? (double)freelist / pages : 0;

                if (ratio < _options.CurrentValue.FreePageRatioThreshold)
                {
                    skipReason = $"free page ratio {ratio:P0} below threshold";
                }
                else if (FreeDiskBytes(dbPath) < sizeBefore)
                {
                    // VACUUM rewrites the database into a temp copy; skip rather than risk filling the disk
                    skipReason = "insufficient free disk space for VACUUM";
                    Log.Warning("Skipping VACUUM for {Database}: not enough free disk space", name);
                }
                else
                {
                    await ExecAsync(conn, "VACUUM", ct);
                    vacuumed = true;
                }
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6) // SQLITE_BUSY, SQLITE_LOCKED
            {
                skipReason = "database busy, retried next run";
                Log.Warning("Database maintenance skipped for {Database}: busy", name);
            }
            catch (Exception ex)
            {
                skipReason = $"error: {ex.Message}";
                Log.Error(ex, "Database maintenance failed for {Database}", name);
            }

            var sizeAfter = File.Exists(dbPath) ? new FileInfo(dbPath).Length : sizeBefore;
            var duration  = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            results.Add(new DatabaseMaintenanceResult(name, analyzed, vacuumed, skipReason, sizeBefore, sizeAfter, duration));

            if (vacuumed)
                Log.Information("Database maintenance: {Database} vacuumed, {Reclaimed} bytes reclaimed in {Duration:F0}ms",
                    name, sizeBefore - sizeAfter, duration);
            else
                Log.Debug("Database maintenance: {Database} analyzed, vacuum skipped ({Reason})", name, skipReason);
        }

        LastRunResults = results;
        LastRunUtc = DateTime.UtcNow;

        var reclaimed = results.Sum(r => r.BytesBefore - r.BytesAfter);
        _audit.Record("maintenance", "database", string.Join(", ", results.Select(r => r.Database)),
            details: $"{results.Count(r => r.Vacuumed)} vacuumed, {reclaimed} bytes reclaimed");

        return results;
    }

    private List<string> ResolveDatabasePaths()
    {
        var cwd = Directory.GetCurrentDirectory();
        var paths = new List<string>
        {
            Path.Combine(cwd, "auth.db"),
            Path.Combine(cwd, "mcp.db"),
            Path.Combine(cwd, "metrics.db")
        };

        var trafficPath = _configuration.GetValue<string>("RequestTrafficLogging:SqlitePath") ?? "log/traffic_logs.db";
        paths.Add(Path.IsPathRooted(trafficPath) ? trafficPath : Path.Combine(cwd, trafficPath));
        return paths;
    }

    private static long FreeDiskBytes(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            return string.IsNullOrEmpty(root) ? long.MaxValue : new DriveInfo(root).AvailableFreeSpace;
        }
        catch
        {
            // If the drive cannot be inspected, do not block maintenance on it
            return long.MaxValue;
        }
    }

    private static async Task ExecAsync(SqliteConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<long> ScalarAsync(SqliteConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result);
    }
}
