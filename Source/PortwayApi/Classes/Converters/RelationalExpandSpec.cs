namespace PortwayApi.Classes;

/// <summary>Resolved metadata for one to-one navigation, ready for EDM emission.
/// Table names are already schema-resolved for the target provider; columns are database names</summary>
public sealed record RelationalExpandSpec(
    string NavName,
    string TargetTable,
    string LocalColumn,
    string TargetColumn,
    IReadOnlyList<string> TargetColumns);
