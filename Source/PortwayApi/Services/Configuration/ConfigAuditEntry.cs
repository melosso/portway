namespace PortwayApi.Services.Configuration;

/// <summary>One recorded configuration change</summary>
public sealed record ConfigAuditEntry(
    long Id,
    string Timestamp,
    string? ClientIp,
    string Action,
    string TargetType,
    string Target,
    string? Details,
    string? BackupPath);
