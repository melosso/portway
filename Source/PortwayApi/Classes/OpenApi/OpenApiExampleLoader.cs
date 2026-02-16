using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Any;

namespace PortwayApi.Classes.Swagger;

/// <summary>
/// Loads and caches Swagger examples from JSON files
/// </summary>
public class SwaggerExampleLoader
{
    private static readonly ConcurrentDictionary<string, IOpenApiAny> _exampleCache = new();
    private readonly string _examplesBasePath;
    private readonly ILogger<SwaggerExampleLoader>? _logger;

    public SwaggerExampleLoader(string? examplesBasePath = null)
    {
        _examplesBasePath = examplesBasePath ?? Path.Combine(Directory.GetCurrentDirectory(), "Endpoints");
        _logger = null; // Logger is optional
    }

    /// <summary>
    /// Load an example from a .example file and cache it
    /// </summary>
    /// <param name="endpointPath">The path to the endpoint (e.g., "Proxy/SalesOrder" or "Proxy/Financial/SalesOrder")</param>
    /// <param name="forceReload">Force reload from disk, bypassing cache</param>
    /// <returns>The parsed OpenAPI example or null if not found</returns>
    public IOpenApiAny? LoadExample(string endpointPath, bool forceReload = false)
    {
        var cacheKey = endpointPath.ToLowerInvariant();

        // Check cache first
        if (!forceReload && _exampleCache.TryGetValue(cacheKey, out var cachedExample))
        {
            _logger?.LogDebug("Loaded example from cache for: {EndpointPath}", endpointPath);
            return cachedExample;
        }

        // Try multiple possible file locations (case-insensitive)
        var candidateFileNames = new[]
        {
            $"{Path.GetFileName(endpointPath)}.example",
            "POST.example",
            "post.example",
            "entity.example",
            "entity.json.example",
            "request.example",
            "example.json",
            "request.example.json"
        };

        // Resolve the endpoint directory in a case-insensitive way, then look for matching files
        var endpointDir = ResolvePathCaseInsensitive(Path.Combine(_examplesBasePath, endpointPath));
        if (!string.IsNullOrEmpty(endpointDir))
        {
            foreach (var candidate in candidateFileNames)
            {
                var resolved = ResolvePathCaseInsensitive(Path.Combine(endpointDir, candidate));
                if (resolved == null)
                    continue;

                try
                {
                    var jsonContent = File.ReadAllText(resolved);
                    var example = ConvertJsonToOpenApiAny(jsonContent);

                    if (example != null)
                    {
                        _exampleCache[cacheKey] = example;
                        _logger?.LogDebug("Loaded and cached example from: {FilePath}", resolved);
                        return example;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to load example from: {FilePath}", resolved);
                }
            }
        }

        _logger?.LogDebug("No example file found for endpoint: {EndpointPath}", endpointPath);
        return null;
    }

    /// <summary>
    /// Load an example specifically for a composite endpoint
    /// </summary>
    /// <param name="compositeEndpointName">Name of the composite endpoint (e.g., "SalesOrder")</param>
    /// <param name="namespacePath">Optional namespace path (e.g., "Financial")</param>
    /// <returns>The parsed OpenAPI example or null if not found</returns>
    public IOpenApiAny? LoadCompositeExample(string compositeEndpointName, string? namespacePath = null)
    {
        // Build the full path
        var endpointPath = string.IsNullOrEmpty(namespacePath)
            ? Path.Combine("Proxy", compositeEndpointName)
            : Path.Combine("Proxy", namespacePath, compositeEndpointName);

        return LoadExample(endpointPath);
    }

    /// <summary>
    /// Convert JSON string to OpenApiAny object
    /// </summary>
    private IOpenApiAny? ConvertJsonToOpenApiAny(string jsonContent)
    {
        if (string.IsNullOrWhiteSpace(jsonContent))
            return null;

        try
        {
            using var document = JsonDocument.Parse(jsonContent);
            return ConvertJsonElementToOpenApiAny(document.RootElement);
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "Invalid JSON in example file");
            return null;
        }
    }

    /// <summary>
    /// Recursively convert JsonElement to OpenApiAny
    /// </summary>
    private IOpenApiAny ConvertJsonElementToOpenApiAny(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var obj = new OpenApiObject();
                foreach (var property in element.EnumerateObject())
                {
                    obj[property.Name] = ConvertJsonElementToOpenApiAny(property.Value);
                }
                return obj;

            case JsonValueKind.Array:
                var array = new OpenApiArray();
                foreach (var item in element.EnumerateArray())
                {
                    array.Add(ConvertJsonElementToOpenApiAny(item));
                }
                return array;

            case JsonValueKind.String:
                return new OpenApiString(element.GetString());

            case JsonValueKind.Number:
                if (element.TryGetInt32(out var intValue))
                    return new OpenApiInteger(intValue);
                if (element.TryGetInt64(out var longValue))
                    return new OpenApiLong(longValue);
                if (element.TryGetDouble(out var doubleValue))
                    return new OpenApiDouble(doubleValue);
                return new OpenApiDouble(element.GetDouble());

            case JsonValueKind.True:
                return new OpenApiBoolean(true);

            case JsonValueKind.False:
                return new OpenApiBoolean(false);

            case JsonValueKind.Null:
                return new OpenApiNull();

            default:
                return new OpenApiString(element.ToString());
        }
    }

    /// <summary>
    /// Clear the example cache
    /// </summary>
    /// <param name="endpointPath">Optional specific endpoint to clear, or null to clear all</param>
    public static void ClearCache(string? endpointPath = null)
    {
        if (endpointPath == null)
        {
            _exampleCache.Clear();
        }
        else
        {
            var cacheKey = endpointPath.ToLowerInvariant();
            _exampleCache.TryRemove(cacheKey, out _);
        }
    }

    /// <summary>
    /// Get count of cached examples
    /// </summary>
    public static int GetCacheCount() => _exampleCache.Count;

    /// <summary>
    /// Check if an example exists for an endpoint
    /// </summary>
    public bool ExampleExists(string endpointPath)
    {
        var cacheKey = endpointPath.ToLowerInvariant();
        if (_exampleCache.ContainsKey(cacheKey))
            return true;

        var endpointDir = ResolvePathCaseInsensitive(Path.Combine(_examplesBasePath, endpointPath));
        if (string.IsNullOrEmpty(endpointDir))
            return false;

        var candidateFileNames = new[]
        {
            "example.json",
            $"{Path.GetFileName(endpointPath)}.example",
            "request.example.json",
            "POST.example",
            "post.example",
            "entity.example",
            "entity.json.example",
            "request.example"
        };

        foreach (var candidate in candidateFileNames)
        {
            if (ResolvePathCaseInsensitive(Path.Combine(endpointDir, candidate)) != null)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Resolve a path in a case-insensitive manner. Returns the actual path if found, otherwise null.
    /// Works for files and directories.
    /// </summary>
    private string? ResolvePathCaseInsensitive(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        // If exact path exists, return it
        if (File.Exists(path) || Directory.Exists(path))
            return path;

        // Split and walk the path segments to match case-insensitively
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (parts.Length == 0)
            return null;

        // Handle UNC or drive root
        var current = parts[0];
        var startIndex = 1;
        if (current.EndsWith(":") || current == "" || current.StartsWith("\\"))
        {
            // it's a drive like C: or root
            current = parts[0];
        }

        for (var i = startIndex; i < parts.Length; i++)
        {
            var segment = parts[i];
            if (string.IsNullOrEmpty(current))
            {
                current = segment;
                continue;
            }

            try
            {
                if (!Directory.Exists(current))
                {
                    // if current is a file, stop
                    if (File.Exists(current))
                        return null;

                    return null;
                }

                var entries = Directory.EnumerateFileSystemEntries(current)
                    .Select(p => Path.GetFileName(p))
                    .Where(n => n != null)
                    .ToList();

                var match = entries.FirstOrDefault(n => string.Equals(n, segment, StringComparison.OrdinalIgnoreCase));
                if (match == null)
                    return null;

                current = Path.Combine(current, match);
            }
            catch
            {
                return null;
            }
        }

        // Final check
        return File.Exists(current) || Directory.Exists(current) ? current : null;
    }

    /// <summary>
    /// Preload examples for multiple endpoints
    /// </summary>
    public void PreloadExamples(IEnumerable<string> endpointPaths)
    {
        foreach (var path in endpointPaths)
        {
            LoadExample(path);
        }
    }
}