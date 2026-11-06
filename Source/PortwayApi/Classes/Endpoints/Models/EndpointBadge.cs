namespace PortwayApi.Classes;

/// <summary>
/// Label on an endpoint's operations in the API reference
/// </summary>
public sealed record EndpointBadge(string Name, string? Position = null, string? Color = null);
