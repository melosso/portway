namespace PortwayApi.Services.Database;

/// <summary>Outcome of one maintenance pass over a single SQLite database</summary>
public sealed record DatabaseMaintenanceResult(
    string Database,
    bool Analyzed,
    bool Vacuumed,
    string? SkipReason,
    long BytesBefore,
    long BytesAfter,
    double DurationMs);
