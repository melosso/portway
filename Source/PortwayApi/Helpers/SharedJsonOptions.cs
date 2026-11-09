using System.Text.Json;

namespace PortwayApi.Helpers;

/// <summary>
/// Serializer options reused across calls; JsonSerializerOptions caches type metadata per instance
/// </summary>
internal static class SharedJsonOptions
{
    public static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };
}
