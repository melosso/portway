namespace PortwayApi.Classes;

/// <summary>
/// Resolved to-one navigation with provider schema tables and database column names
/// </summary>
public sealed record RelationalExpandSpec(
    string NavName,
    string TargetTable,
    string LocalColumn,
    string TargetColumn,
    IReadOnlyList<string> TargetColumns);
