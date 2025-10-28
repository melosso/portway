namespace PortwayApi.Classes.Helpers;

using System.Text.Json;
using Serilog;

/// <summary>
/// Helper class for translating HTTP methods based on custom properties
/// </summary>
public static class HttpMethodTranslator
{
    /// <summary>
    /// Translates an HTTP method based on the HttpMethodTranslation custom property
    /// </summary>
    /// <param name="originalMethod">The original HTTP method (e.g., "PUT")</param>
    /// <param name="customProperties">Custom properties from the endpoint definition</param>
    /// <returns>The translated HTTP method or the original method if no translation is configured</returns>
    public static string TranslateMethod(string originalMethod, Dictionary<string, object>? customProperties)
    {
        if (customProperties == null || !customProperties.ContainsKey("HttpMethodTranslation"))
        {
            return originalMethod;
        }

        try
        {
            var translationConfig = customProperties["HttpMethodTranslation"];
            
            // Handle both string and JsonElement types
            string translationString;
            if (translationConfig is JsonElement jsonElement)
            {
                translationString = jsonElement.GetString() ?? string.Empty;
            }
            else if (translationConfig is string str)
            {
                translationString = str;
            }
            else
            {
                Log.Warning("HttpMethodTranslation custom property is not a string: {Type}", translationConfig?.GetType().Name);
                return originalMethod;
            }

            if (string.IsNullOrWhiteSpace(translationString))
            {
                return originalMethod;
            }

            // Parse translation mappings in format "FROM;TO,FROM2;TO2"
            var translations = ParseTranslationMappings(translationString);
            
            if (translations.TryGetValue(originalMethod.ToUpper(), out var translatedMethod))
            {
                Log.Debug("Translating HTTP method: {OriginalMethod} -> {TranslatedMethod}", originalMethod, translatedMethod);
                return translatedMethod;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error translating HTTP method {Method} with custom properties", originalMethod);
        }

        return originalMethod;
    }

    /// <summary>
    /// Parses translation mappings from a string format like "PUT:MERGE,POST:CREATE" (preferred) or "PUT;MERGE,POST;CREATE" (legacy)
    /// </summary>
    /// <param name="translationString">The translation configuration string</param>
    /// <returns>Dictionary mapping original methods to translated methods</returns>
    private static Dictionary<string, string> ParseTranslationMappings(string translationString)
    {
        var translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(translationString))
        {
            return translations;
        }

        // Split by comma to get individual mappings
        var mappings = translationString.Split(',', StringSplitOptions.RemoveEmptyEntries);

        foreach (var mapping in mappings)
        {
            // Try colon first (preferred format), then semicolon (legacy format)
            var parts = mapping.Split(':', StringSplitOptions.RemoveEmptyEntries);
            
            if (parts.Length != 2)
            {
                // Fall back to semicolon for backward compatibility
                parts = mapping.Split(';', StringSplitOptions.RemoveEmptyEntries);
            }
            
            if (parts.Length == 2)
            {
                var fromMethod = parts[0].Trim().ToUpper();
                var toMethod = parts[1].Trim().ToUpper();
                
                if (!string.IsNullOrWhiteSpace(fromMethod) && !string.IsNullOrWhiteSpace(toMethod))
                {
                    translations[fromMethod] = toMethod;
                    Log.Debug("Parsed HTTP method translation: {From} -> {To}", fromMethod, toMethod);
                }
            }
            else
            {
                Log.Warning("Invalid HTTP method translation mapping format: {Mapping}. Expected format: 'FROM:TO' or 'FROM;TO'", mapping);
            }
        }

        return translations;
    }

    /// <summary>
    /// Validates that a translated HTTP method is supported
    /// </summary>
    /// <param name="method">The HTTP method to validate</param>
    /// <returns>True if the method is supported, false otherwise</returns>
    public static bool IsValidHttpMethod(string method)
    {
        var validMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "GET", "POST", "PUT", "DELETE", "PATCH", "MERGE", "HEAD", "OPTIONS"
        };

        return validMethods.Contains(method);
    }
}