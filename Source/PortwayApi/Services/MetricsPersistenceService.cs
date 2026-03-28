namespace PortwayApi.Services;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Serilog;

/// <summary>
/// Background service that persists in-memory request metrics to SQLite and hydrates the
/// in-memory buffer on startup so 7-day / 30-day chart periods survive restarts.
/// </summary>
public sealed class MetricsPersistenceService : BackgroundService
{
    private readonly MetricsService _metrics;
    private readonly string _connectionString;

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

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS RequestMetrics (
                Id         INTEGER PRIMARY KEY AUTOINCREMENT,
                Timestamp  TEXT    NOT NULL,
                StatusCode INTEGER NOT NULL,
                Method     TEXT    NOT NULL,
                Source     TEXT    NOT NULL DEFAULT 'api',
                Endpoint   TEXT    NOT NULL DEFAULT ''
            );
            CREATE INDEX IF NOT EXISTS idx_rm_ts ON RequestMetrics(Timestamp);
            """;
        cmd.ExecuteNonQuery();

        // Migration: add Source/Endpoint columns to any pre-existing table
        foreach (var col in new[] { "Source TEXT NOT NULL DEFAULT 'api'", "Endpoint TEXT NOT NULL DEFAULT ''" })
        {
            try
            {
                cmd.CommandText = $"ALTER TABLE RequestMetrics ADD COLUMN {col}";
                cmd.ExecuteNonQuery();
            }
            catch { /* column already exists — safe to ignore */ }
        }
    }

    // Startup hydration
    private async Task HydrateAsync(CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-31).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var entries = new List<MetricsService.RequestEntry>();

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Timestamp, StatusCode, Method, Source, Endpoint FROM RequestMetrics WHERE Timestamp > @cutoff ORDER BY Timestamp";
        cmd.Parameters.AddWithValue("@cutoff", cutoff);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (DateTime.TryParse(reader.GetString(0), null, System.Globalization.DateTimeStyles.RoundtripKind, out var ts))
                entries.Add(new MetricsService.RequestEntry(
                    ts,
                    reader.GetInt32(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? "api" : reader.GetString(3),
                    reader.IsDBNull(4) ? ""    : reader.GetString(4)));
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

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO RequestMetrics (Timestamp, StatusCode, Method, Source, Endpoint) VALUES (@ts, @sc, @m, @src, @ep)";
            var pTs  = cmd.CreateParameter(); pTs.ParameterName  = "@ts";  cmd.Parameters.Add(pTs);
            var pSc  = cmd.CreateParameter(); pSc.ParameterName  = "@sc";  cmd.Parameters.Add(pSc);
            var pM   = cmd.CreateParameter(); pM.ParameterName   = "@m";   cmd.Parameters.Add(pM);
            var pSrc = cmd.CreateParameter(); pSrc.ParameterName = "@src"; cmd.Parameters.Add(pSrc);
            var pEp  = cmd.CreateParameter(); pEp.ParameterName  = "@ep";  cmd.Parameters.Add(pEp);

            foreach (var e in batch)
            {
                pTs.Value  = e.Timestamp.ToString("yyyy-MM-ddTHH:mm:ssZ");
                pSc.Value  = e.StatusCode;
                pM.Value   = e.Method;
                pSrc.Value = e.Source;
                pEp.Value  = e.Endpoint;
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "MetricsPersistenceService: error writing batch of {Count}", batch.Count);
        }
    }

    private async Task PruneOldRowsAsync(CancellationToken ct = default)
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-31).ToString("yyyy-MM-ddTHH:mm:ssZ");
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM RequestMetrics WHERE Timestamp < @cutoff";
            cmd.Parameters.AddWithValue("@cutoff", cutoff);
            var deleted = await cmd.ExecuteNonQueryAsync(ct);
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
