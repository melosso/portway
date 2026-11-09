namespace PortwayApi.Helpers;

/// <summary>
/// Read options of GET or QUERY
/// </summary>
public sealed record ODataReadOptions(string? Select, string? Filter, string? OrderBy, string? Expand, bool Count, int Top, int Skip);
