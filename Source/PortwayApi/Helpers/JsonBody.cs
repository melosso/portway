using System.Text.Json;

namespace PortwayApi.Helpers;

public static class JsonBody
{
    public const string InvalidMessage = "Request body must be valid JSON.";

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
