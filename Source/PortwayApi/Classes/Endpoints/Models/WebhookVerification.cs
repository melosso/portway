using System.Text.Json;
using System.Text.Json.Serialization;

namespace PortwayApi.Classes;

/// <summary>
/// Verification of one webhook id, as written in entity.json
/// </summary>
public sealed record WebhookVerification
{
    public string? Scheme { get; init; }
    public string? Secret { get; init; }
    public string? PreviousSecret { get; init; }
    public string? Header { get; init; }
    public string? Username { get; init; }
    public string? Prefix { get; init; }
    public string? Encoding { get; init; }
    public string? KeyEncoding { get; init; }
    public string? SignedContent { get; init; }
    public string? TimestampHeader { get; init; }
    public string? IdHeader { get; init; }
    public int? ToleranceSeconds { get; init; }
    public string? Format { get; init; }
    public string? DeliveryIdHeader { get; init; }
    public string? DeliveryIdField { get; init; }
    public string? DedupeKey { get; init; }
    public bool Deduplicate { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; init; }
}
