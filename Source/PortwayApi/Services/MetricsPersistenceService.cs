namespace PortwayApi.Services;

using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Serilog;

/// <summary>Background service that persists in-memory request metrics to SQLite and hydrates the in-memory buffer on startup so 7-day / 30-day chart periods survive restarts</summary>
public sealed class MetricsPersistenceService : BackgroundService
{
    private readonly MetricsService _metrics;
    private readonly string _connectionString;

    // Dapper row shape for hydration, Timestamp stays a string until parsed with RoundtripKind
    private sealed record MetricRow(string Timestamp, int StatusCode, string Method, string? Source, string? Endpoint);

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
        foreach (var alterSql in new[]
        {
            "ALTER TABLE RequestMetrics ADD COLUMN Source TEXT NOT NULL DEFAULT 'api'",
            "ALTER TABLE RequestMetrics ADD COLUMN Endpoint TEXT NOT NULL DEFAULT ''",
        })
        {
            try { conn.Execute(alterSql); }
            catch { /* column already exists, safe to ignore */ }
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
            "SELECT Timestamp, StatusCode, Method, Source, Endpoint FROM RequestMetrics WHERE Timestamp > @cutoff ORDER BY Timestamp",
            new { cutoff }, cancellationToken: ct));

        foreach (var row in rows)
        {
            if (DateTime.TryParse(row.Timestamp, null, System.Globalization.DateTimeStyles.RoundtripKind, out var ts))
                entries.Add(new MetricsService.RequestEntry(
                    ts, row.StatusCode, row.Method, row.Source ?? "api", row.Endpoint ?? ""));
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
                "INSERT INTO RequestMetrics (Timestamp, StatusCode, Method, Source, Endpoint) VALUES (@ts, @sc, @m, @src, @ep)",
                batch.Select(e => new
                {
                    ts  = e.Timestamp.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    sc  = e.StatusCode,
                    m   = e.Method,
                    src = e.Source,
                    ep  = e.Endpoint
                }),
                transaction: tx, cancellationToken: ct));

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
