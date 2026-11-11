using System.Text.Json;

namespace PortwayApi.Helpers;

/// <summary>
/// Request body parsing shared by every JSON write route
/// </summary>
public static class JsonBody
{
    public const string InvalidMessage = "Request body must be valid JSON.";

    /// <summary>
    /// Parses <paramref name="body"/>. False for an empty or malformed body.
    /// </summary>
    public static bool TryParse(string body, out JsonElement element)
    {
        element = default;
        if (string.IsNullOrWhiteSpace(body))
            return false;

        try
        {
            element = JsonSerializer.Deserialize<JsonElement>(body);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
