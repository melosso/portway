namespace PortwayApi.Services;

using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Serilog;

/// <summary>
/// Background service that persists in-memory request metrics to SQLite and hydrates the in-memory buffer on startup so 7-day / 30-day chart periods survive restarts
/// </summary>
public sealed class MetricsPersistenceService : BackgroundService
{
    private readonly MetricsService _metrics;
    private readonly string _connectionString;

    // Dapper row shape for hydration, SQLite INTEGER surfaces as Int64 so StatusCode is long
    private sealed record MetricRow(string Timestamp, long StatusCode, string Method, string? Source, string? Endpoint,
        string? Environment, string? Version, long? DurationMs);

    private const int BatchSize = 50;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);
    private const int PruneEveryNFlushes = 200;

    public MetricsPersistenceService(MetricsService metrics)
    {
        _metrics = metrics;
        var dbPath = Path.Combine(Directory.GetCurrentDirectory(), "metrics.db");
        _connectionString = $"Data Source={dbPath};Cache=Shared";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            EnsureDbCreated();
            await HydrateAsync(stoppingToken);
            await ProcessChannelAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error(ex, "MetricsPersistenceService encountered a fatal error");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await FlushRemainingAsync(cancellationToken);
    }

    // Database init / migration
    private void EnsureDbCreated()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        conn.Execute("""
            CREATE TABLE IF NOT EXISTS RequestMetrics (
                Id         INTEGER PRIMARY KEY AUTOINCREMENT,
                Timestamp  TEXT    NOT NULL,
                StatusCode INTEGER NOT NULL,
                Method     TEXT    NOT NULL,
                Source     TEXT    NOT NULL DEFAULT 'api',
                Endpoint   TEXT    NOT NULL DEFAULT ''
            );
            CREATE INDEX IF NOT EXISTS idx_rm_ts ON RequestMetrics(Timestamp);
            """);

        // Migration: add Source/Endpoint columns to any pre-existing table
        var migrations = new (string Column, string AlterSql)[]
        {
            ("Source", "ALTER TABLE RequestMetrics ADD COLUMN Source TEXT NOT NULL DEFAULT 'api'"),
            ("Endpoint", "ALTER TABLE RequestMetrics ADD COLUMN Endpoint TEXT NOT NULL DEFAULT ''"),
            ("Environment", "ALTER TABLE RequestMetrics ADD COLUMN Environment TEXT NOT NULL DEFAULT ''"),
            ("Version", "ALTER TABLE RequestMetrics ADD COLUMN Version TEXT NOT NULL DEFAULT ''"),
            ("DurationMs", "ALTER TABLE RequestMetrics ADD COLUMN DurationMs INTEGER NULL"),
        };

        foreach (var (column, alterSql) in migrations)
        {
            var exists = conn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM pragma_table_info('RequestMetrics') WHERE name=@name", new { name = column });

            if (exists == 0)
            {
                conn.Execute(alterSql);
                Log.Information("Added {Column} column to RequestMetrics table", column);
            }
        }
    }

    // Startup hydration
    private async Task HydrateAsync(CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-31).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var entries = new List<MetricsService.RequestEntry>();

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var rows = await conn.QueryAsync<MetricRow>(new CommandDefinition(
            "SELECT Timestamp, StatusCode, Method, Source, Endpoint, Environment, Version, DurationMs FROM RequestMetrics WHERE Timestamp > @cutoff ORDER BY Timestamp",
            new { cutoff }, cancellationToken: ct));

        foreach (var row in rows)
        {
            if (DateTime.TryParse(row.Timestamp, null, System.Globalization.DateTimeStyles.RoundtripKind, out var ts))
                entries.Add(new MetricsService.RequestEntry(
                    ts, (int)row.StatusCode, row.Method, row.Source ?? "api", row.Endpoint ?? "",
                    row.Environment ?? "", row.Version ?? "", (int?)row.DurationMs));
        }

        if (entries.Count > 0)
        {
            _metrics.Hydrate(entries);
            Log.Debug("MetricsPersistenceService: hydrated {Count} entries from metrics.db", entries.Count);
        }
    }

    // Channel processing loop
    private async Task ProcessChannelAsync(CancellationToken ct)
    {
        var reader = _metrics.PersistenceChannel.Reader;
        var batch = new List<MetricsService.RequestEntry>(BatchSize);
        int flushCount = 0;

        while (!ct.IsCancellationRequested)
        {
            using var timeoutCts = new CancellationTokenSource(FlushInterval);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, ct);

            try { await reader.WaitToReadAsync(linkedCts.Token); }
            catch (OperationCanceledException) { if (ct.IsCancellationRequested) break; }

            while (batch.Count < BatchSize && reader.TryRead(out var entry))
                batch.Add(entry);

            if (batch.Count == 0) continue;

            await WriteBatchAsync(batch, ct);
            flushCount++;
            batch.Clear();

            if (flushCount % PruneEveryNFlushes == 0)
                await PruneOldRowsAsync(ct);
        }
    }

    // SQLite helpers
    private async Task WriteBatchAsync(List<MetricsService.RequestEntry> batch, CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var tx = conn.BeginTransaction();

            // Dapper executes the insert once per element of the sequence
            await conn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO RequestMetrics (Timestamp, StatusCode, Method, Source, Endpoint, Environment, Version, DurationMs) VALUES (@ts, @sc, @m, @src, @ep, @env, @ver, @ms)",
                batch.Where(e => e.Timestamp >= _metrics.ClearedAt).Select(e => new
                {
                    ts = e.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ"),
                    sc = e.StatusCode,
                    m = e.Method,
                    src = e.Source,
                    ep = e.Endpoint,
                    env = e.Environment,
                    ver = e.Version,
                    ms = e.DurationMs
                }),
                transaction: tx, cancellationToken: ct));

            await tx.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "MetricsPersistenceService: error writing batch of {Count}", batch.Count);
        }
    }

    public async Task ClearAsync(DateTime before, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("DELETE FROM RequestMetrics WHERE Timestamp < @before",
            new { before = before.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ") }, cancellationToken: ct));
    }

    private async Task PruneOldRowsAsync(CancellationToken ct = default)
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-31).ToString("yyyy-MM-ddTHH:mm:ssZ");
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);
            var deleted = await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM RequestMetrics WHERE Timestamp < @cutoff", new { cutoff }, cancellationToken: ct));
            if (deleted > 0)
                Log.Debug("MetricsPersistenceService: pruned {Count} old metric rows", deleted);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "MetricsPersistenceService: error pruning old rows");
        }
    }

    private async Task FlushRemainingAsync(CancellationToken ct = default)
    {
        var reader = _metrics.PersistenceChannel.Reader;
        var remaining = new List<MetricsService.RequestEntry>();
        while (reader.TryRead(out var e)) remaining.Add(e);

        if (remaining.Count > 0)
        {
            await WriteBatchAsync(remaining, ct);
            Log.Debug("MetricsPersistenceService: flushed {Count} entries on shutdown", remaining.Count);
        }
    }
}
