namespace PortwayApi.Classes.Helpers;

using System.Text.Json;
using Serilog;

/// <summary>
/// Defines how to handle conflicts when custom headers already exist
/// </summary>
public enum HeaderConflictResolution
{
    /// <summary>
    /// Skip adding the custom header if it already exists
    /// </summary>
    Skip,
    
    /// <summary>
    /// Overwrite the existing header with the custom value
    /// </summary>
    Overwrite,
    
    /// <summary>
    /// Log the conflict but still add the header (may create duplicates)
    /// </summary>
    LogAndAdd
}

/// <summary>
/// Helper class for appending HTTP headers based on custom properties and method translation
/// </summary>
public static class HttpMethodHeaderAppender
{
    /// <summary>
    /// Gets additional headers to append based on the HttpMethodAppendHeaders custom property
    /// </summary>
    /// <param name="originalMethod">The original HTTP method before translation (e.g., "PUT")</param>
    /// <param name="translatedMethod">The translated HTTP method (e.g., "POST")</param>
    /// <param name="customProperties">Custom properties from the endpoint definition</param>
    /// <param name="existingHeaders">Optional set of existing header names to check for conflicts</param>
    /// <param name="conflictResolution">How to handle header conflicts (Skip, Overwrite, or Log)</param>
    /// <returns>Dictionary of headers to append to the request</returns>
    public static Dictionary<string, string> GetAppendHeaders(
        string originalMethod, 
        string translatedMethod, 
        Dictionary<string, object>? customProperties,
        IEnumerable<string>? existingHeaders = null,
        HeaderConflictResolution conflictResolution = HeaderConflictResolution.Skip)
    {
        var headers = new Dictionary<string, string>();

        if (customProperties == null || !customProperties.ContainsKey("HttpMethodAppendHeaders"))
        {
            return headers;
        }

        // Create a set of existing headers for efficient lookup (case-insensitive)
        var existingHeaderSet = existingHeaders?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>();

        try
        {
            var appendHeadersConfig = customProperties["HttpMethodAppendHeaders"];
            
            // Handle both string and JsonElement types
            string appendHeadersString;
            if (appendHeadersConfig is JsonElement jsonElement)
            {
                appendHeadersString = jsonElement.GetString() ?? string.Empty;
            }
            else if (appendHeadersConfig is string str)
            {
                appendHeadersString = str;
            }
            else
            {
                Log.Warning("HttpMethodAppendHeaders custom property is not a string: {Type}", appendHeadersConfig?.GetType().Name);
                return headers;
            }

            if (string.IsNullOrWhiteSpace(appendHeadersString))
            {
                return headers;
            }

            // Parse header append mappings
            var headerMappings = ParseHeaderAppendMappings(appendHeadersString);
            
            // Check if we have mappings for the original method
            if (headerMappings.TryGetValue(originalMethod.ToUpper(), out var methodHeaders))
            {
                foreach (var header in methodHeaders)
                {
                    // Support dynamic values - replace {ORIGINAL_METHOD} and {TRANSLATED_METHOD}
                    var headerValue = header.Value
                        .Replace("{ORIGINAL_METHOD}", originalMethod.ToUpper())
                        .Replace("{TRANSLATED_METHOD}", translatedMethod.ToUpper());
                    
                    // Check for conflicts with existing headers
                    if (existingHeaderSet.Contains(header.Key))
                    {
                        switch (conflictResolution)
                        {
                            case HeaderConflictResolution.Skip:
                                Log.Debug("Skipping custom header {HeaderKey} because it already exists (user-provided)", header.Key);
                                continue;
                                
                            case HeaderConflictResolution.Overwrite:
                                Log.Debug("Overwriting existing header {HeaderKey} with custom value: {HeaderValue}", header.Key, headerValue);
                                break;
                                
                            case HeaderConflictResolution.LogAndAdd:
                                Log.Warning("Header conflict detected: {HeaderKey} already exists but adding custom value anyway: {HeaderValue}", header.Key, headerValue);
                                break;
                        }
                    }
                    
                    headers[header.Key] = headerValue;
                    Log.Debug("Appending header for method {OriginalMethod}: {HeaderKey}={HeaderValue}", 
                        originalMethod, header.Key, headerValue);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error processing HttpMethodAppendHeaders for method {Method} with custom properties", originalMethod);
        }

        return headers;
    }

    /// <summary>
    /// Parses header append mappings from a string format like "PUT:X-HTTP-Method={ORIGINAL_METHOD},Content-Type=application/merge-patch+json"
    /// </summary>
    /// <param name="appendHeadersString">The header append configuration string</param>
    /// <returns>Dictionary mapping HTTP methods to their additional headers</returns>
    private static Dictionary<string, Dictionary<string, string>> ParseHeaderAppendMappings(string appendHeadersString)
    {
        var mappings = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(appendHeadersString))
        {
            return mappings;
        }

        // Split by semicolon to get individual method mappings
        var methodMappings = appendHeadersString.Split(';', StringSplitOptions.RemoveEmptyEntries);

        foreach (var methodMapping in methodMappings)
        {
            // Split by colon to get method:headers mapping
            var parts = methodMapping.Split(':', 2, StringSplitOptions.RemoveEmptyEntries);
            
            if (parts.Length == 2)
            {
                var method = parts[0].Trim().ToUpper();
                var headersString = parts[1].Trim();
                
                if (!string.IsNullOrWhiteSpace(method) && !string.IsNullOrWhiteSpace(headersString))
                {
                    var methodHeaders = ParseMethodHeaders(headersString);
                    if (methodHeaders.Count > 0)
                    {
                        mappings[method] = methodHeaders;
                        Log.Debug("Parsed HTTP method header mappings for {Method}: {Headers}", 
                            method, string.Join(", ", methodHeaders.Select(h => $"{h.Key}={h.Value}")));
                    }
                }
            }
            else
            {
                Log.Warning("Invalid HttpMethodAppendHeaders method mapping format: {Mapping}. Expected format: 'METHOD:header1=value1,header2=value2'", methodMapping);
            }
        }

        return mappings;
    }

    /// <summary>
    /// Parses headers for a specific method from comma-separated format like "X-HTTP-Method={ORIGINAL_METHOD},Content-Type=application/merge-patch+json"
    /// </summary>
    /// <param name="headersString">The headers string for a specific method</param>
    /// <returns>Dictionary of header name to header value</returns>
    private static Dictionary<string, string> ParseMethodHeaders(string headersString)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(headersString))
        {
            return headers;
        }

        // Split by comma to get individual headers
        var headerPairs = headersString.Split(',', StringSplitOptions.RemoveEmptyEntries);

        foreach (var headerPair in headerPairs)
        {
            // Split by equals to get header name and value
            var parts = headerPair.Split('=', 2, StringSplitOptions.RemoveEmptyEntries);
            
            if (parts.Length == 2)
            {
                var headerName = parts[0].Trim();
                var headerValue = parts[1].Trim();
                
                if (!string.IsNullOrWhiteSpace(headerName) && !string.IsNullOrWhiteSpace(headerValue))
                {
                    headers[headerName] = headerValue;
                    Log.Debug("Parsed header: {HeaderName}={HeaderValue}", headerName, headerValue);
                }
            }
            else
            {
                Log.Warning("Invalid header format: {HeaderPair}. Expected format: 'HeaderName=HeaderValue'", headerPair);
            }
        }

        return headers;
    }

    /// <summary>
    /// Validates that header names are valid HTTP header names
    /// </summary>
    /// <param name="headerName">The header name to validate</param>
    /// <returns>True if the header name is valid, false otherwise</returns>
    public static bool IsValidHeaderName(string headerName)
    {
        if (string.IsNullOrWhiteSpace(headerName))
            return false;

        // Basic validation - HTTP header names should not contain spaces, control characters, etc.
        // For simplicity, we'll allow alphanumeric, hyphens, and underscores
        return headerName.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_');
    }
}