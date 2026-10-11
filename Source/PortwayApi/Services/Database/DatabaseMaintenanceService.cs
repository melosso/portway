using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using PortwayApi.Helpers;
using PortwayApi.Services.Configuration;
using Serilog;

namespace PortwayApi.Services.Database;

/// <summary>
/// Nightly SQLite self-tuning: ANALYZE refreshes index statistics, VACUUM reclaims space when a database is bloated
/// </summary>
public class DatabaseMaintenanceService : BackgroundService
{
    private readonly IOptionsMonitor<DatabaseMaintenanceOptions> _options;
    private readonly IConfiguration _configuration;
    private readonly ConfigAuditService _audit;
    private readonly string _statePath;

    // one pass at a time, scheduled or started from the console
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Results of the most recent run, kept across restarts; empty until the first run completes
    /// </summary>
    public IReadOnlyList<DatabaseMaintenanceResult> LastRunResults { get; private set; } = [];
    public DateTime? LastRunUtc { get; private set; }

    public DatabaseMaintenanceService(
        IOptionsMonitor<DatabaseMaintenanceOptions> options,
        IConfiguration configuration,
        ConfigAuditService audit,
        string? stateDirectory = null)
    {
        _options = options;
        _configuration = configuration;
        _audit = audit;
        _statePath = Path.Combine(stateDirectory ?? Path.Combine(Directory.GetCurrentDirectory(), ".core"), "maintenance.json");
        LoadState();
    }

    private sealed record MaintenanceState(DateTime LastRunUtc, List<DatabaseMaintenanceResult> Results);

    private void LoadState()
    {
        try
        {
            if (!File.Exists(_statePath)) return;
            var state = JsonSerializer.Deserialize<MaintenanceState>(File.ReadAllText(_statePath));
            if (state is null) return;
            LastRunUtc = state.LastRunUtc;
            LastRunResults = state.Results;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Could not read the last maintenance run from {Path}", _statePath);
        }
    }

    private void SaveState()
    {
        try
        {
            HiddenDirectoryHelper.Ensure(Path.GetDirectoryName(_statePath)!);
            var temp = _statePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new MaintenanceState(LastRunUtc!.Value, [.. LastRunResults])));
            File.Move(temp, _statePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Could not save the last maintenance run to {Path}", _statePath);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.CurrentValue.RunOnStartup)
        {
            // Small delay so startup IO (EnsureCreated, token bootstrap) settles first
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            await SafeRunOnceAsync(stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = DelayUntilNextRun(_options.CurrentValue.Schedule);
            await Task.Delay(delay, stoppingToken);

            await SafeRunOnceAsync(stoppingToken);
        }
    }

    // .NET 11 stops the host on an unhandled BackgroundService exception; keep a maintenance failure contained
    private async Task SafeRunOnceAsync(CancellationToken ct)
    {
        try
        {
            if (_options.CurrentValue.Enabled)
                await RunOnceAsync(ct);
            if (_options.CurrentValue.Backup.Enabled)
                await CreateBackupAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw; // shutdown path; let the host stop normally
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Database maintenance pass failed; the service will retry on the next schedule");
        }
    }

    /// <summary>
    /// Time until the next occurrence of the configured local time of day; falls back to 03:00 on bad input
    /// </summary>
    internal static TimeSpan DelayUntilNextRun(string schedule, DateTime? nowLocal = null)
    {
        if (!TimeSpan.TryParse(schedule, out var timeOfDay) || timeOfDay < TimeSpan.Zero || timeOfDay >= TimeSpan.FromDays(1))
            timeOfDay = TimeSpan.FromHours(3);

        var now = nowLocal ?? DateTime.Now;
        var next = now.Date.Add(timeOfDay);
        if (next <= now) next = next.AddDays(1);
        return next - now;
    }

    /// <summary>
    /// Runs one maintenance pass over all Portway SQLite databases that exist on disk
    /// </summary>
    public Task<List<DatabaseMaintenanceResult>> RunOnceAsync(CancellationToken ct = default) => RunGatedAsync(false, ct);

    /// <summary>
    /// Runs one pass on operator request and compacts every database regardless of the free page threshold
    /// </summary>
    public Task<List<DatabaseMaintenanceResult>> RunNowAsync(CancellationToken ct = default) => RunGatedAsync(true, ct);

    private async Task<List<DatabaseMaintenanceResult>> RunGatedAsync(bool alwaysVacuum, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return await RunPassAsync(alwaysVacuum, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Writes a snapshot archive and applies the retention setting
    /// </summary>
    public async Task<string> CreateBackupAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var name = await BackupArchive.CreateAsync(Directory.GetCurrentDirectory(), _options.CurrentValue.Backup.Retention, ct);
            _audit.Record("backup", "database", name);
            Log.Information("Backup {Name} written to .backups/snapshots", name);
            return name;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<DatabaseMaintenanceResult>> RunPassAsync(bool alwaysVacuum, CancellationToken ct)
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
            string? skipReason = null, integrity = null;

            try
            {
                await using var conn = new SqliteConnection($"Data Source={dbPath}");
                await conn.OpenAsync(ct);
                await ExecAsync(conn, "PRAGMA busy_timeout = 5000", ct);

                // Flush the WAL so page counts reflect the main database file
                await ExecAsync(conn, "PRAGMA wal_checkpoint(TRUNCATE)", ct);

                integrity = await QuickCheckAsync(conn, ct);
                if (integrity != "ok")
                    throw new InvalidOperationException($"integrity check failed: {integrity}");

                await ExecAsync(conn, "ANALYZE", ct);
                analyzed = true;

                var freelist = await ScalarAsync(conn, "PRAGMA freelist_count", ct);
                var pages = await ScalarAsync(conn, "PRAGMA page_count", ct);
                var ratio = pages > 0 ? (double)freelist / pages : 0;

                if (!alwaysVacuum && ratio < _options.CurrentValue.FreePageRatioThreshold)
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
                integrity ??= ex.Message;
                skipReason = $"error: {ex.Message}";
                Log.Error(ex, "Database maintenance failed for {Database}", name);
            }

            var sizeAfter = File.Exists(dbPath) ? new FileInfo(dbPath).Length : sizeBefore;
            var duration = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            results.Add(new DatabaseMaintenanceResult(name, analyzed, vacuumed, skipReason, sizeBefore, sizeAfter, duration, integrity));

            if (vacuumed)
                Log.Information("Database maintenance: {Database} vacuumed, {Reclaimed} bytes reclaimed in {Duration:F0}ms",
                    name, sizeBefore - sizeAfter, duration);
            else
                Log.Debug("Database maintenance: {Database} analyzed, vacuum skipped ({Reason})", name, skipReason);
        }

        LastRunResults = results;
        LastRunUtc = DateTime.UtcNow;
        SaveState();

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

    // first rows of PRAGMA quick_check, a single "ok" when the database is intact
    private static async Task<string> QuickCheckAsync(SqliteConnection conn, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA quick_check(5)";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var rows = new List<string>();
        while (await reader.ReadAsync(ct)) rows.Add(reader.GetString(0));
        return string.Join("; ", rows);
    }

    private static async Task<long> ScalarAsync(SqliteConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result);
    }
}
