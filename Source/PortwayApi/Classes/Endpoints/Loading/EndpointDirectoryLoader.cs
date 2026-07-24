using PortwayApi.Helpers;
using Serilog;
using System.Text.Json;

namespace PortwayApi.Classes;

/// <summary>Single directory-scan loader shared by the Proxy, SQL, Static and File endpoint types</summary>
internal static class EndpointDirectoryLoader
{
    public static Dictionary<string, EndpointDefinition> Load(string endpointsDirectory, EndpointLoaderSpec spec)
    {
        var endpoints = new Dictionary<string, EndpointDefinition>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (!Directory.Exists(endpointsDirectory))
            {
                Log.Warning($"{spec.TypeLabel} endpoints directory not found: {endpointsDirectory}");
                Directory.CreateDirectory(endpointsDirectory);
                return endpoints;
            }

            foreach (var file in Directory.GetFiles(endpointsDirectory, spec.SearchPattern, SearchOption.AllDirectories))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var definition = spec.Parse(json);

                    if (definition == null || !spec.IsValid(definition))
                    {
                        Log.Warning($"Failed to load {spec.FailPrefix}endpoint from {{File}}", file);
                        continue;
                    }

                    var key = spec.NamespaceAware
                        ? BuildNamespacedKey(file, endpointsDirectory, definition)
                        : BuildFlatKey(file, definition);
                    if (key == null) continue;

                    endpoints[key] = definition;
                    spec.LogLoaded(key, definition);
                }
                catch (JsonException ex)
                {
                    Log.Warning($"Invalid JSON in {spec.LowerLabel} endpoint {{File}}: {{Message}}", file, ex.Message);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"Unexpected error reading {spec.LowerLabel} endpoint file: {{File}}", file);
                }
            }

            Log.Debug($"Loaded {endpoints.Count} {spec.LowerLabel} endpoints from {endpointsDirectory}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Error scanning {spec.LowerLabel} endpoints directory: {{Directory}}", endpointsDirectory);
        }

        return endpoints;
    }

    /// <summary>Namespace-aware key: folder inference, explicit-namespace warnings, validation, "{ns}/{name}" routing key</summary>
    private static string? BuildNamespacedKey(string file, string endpointsDirectory, EndpointDefinition definition)
    {
        var (inferredNamespace, endpointName) = DirectoryHelper.ExtractNamespaceAndEndpoint(file, endpointsDirectory);

        // Inferred namespace is a fallback; entity.json Namespace takes precedence
        definition.InferredNamespace = inferredNamespace;

        // Folder name kept for backward compatibility with DocumentationTag logic
        definition.FolderName = endpointName;

        if (string.IsNullOrWhiteSpace(endpointName))
        {
            Log.Warning("Could not determine endpoint name for {File}", file);
            return null;
        }

        // Warn when entity.json Namespace overrides or doubles the folder-inferred namespace
        if (!string.IsNullOrEmpty(definition.Namespace))
        {
            if (!string.IsNullOrEmpty(inferredNamespace))
            {
                if (!string.Equals(definition.Namespace, inferredNamespace, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Warning(
                        "Endpoint {EndpointName}: entity.json Namespace '{Explicit}' overrides folder namespace '{Inferred}' — routing key will be '{Explicit}/{EndpointName}'",
                        endpointName, definition.Namespace, inferredNamespace);
                }
            }
            else if (string.Equals(definition.Namespace, endpointName, StringComparison.OrdinalIgnoreCase))
            {
                Log.Warning(
                    "Endpoint {EndpointName}: entity.json Namespace '{Namespace}' matches the folder name — routing key will be '{Namespace}/{EndpointName}' (doubled). Remove Namespace from entity.json to use key '{EndpointName}'",
                    endpointName, definition.Namespace);
            }
            else
            {
                Log.Warning(
                    "Endpoint {EndpointName}: entity.json Namespace '{Namespace}' overrides flat folder identity — routing key will be '{Namespace}/{EndpointName}'",
                    endpointName, definition.Namespace);
            }
        }

        var validationErrors = definition.ValidateNamespace();
        if (validationErrors.Count != 0)
        {
            Log.Warning("Namespace validation failed for {File}: {Errors}", file, string.Join(", ", validationErrors));
            return null;
        }

        var effectiveNamespace = definition.EffectiveNamespace;
        return !string.IsNullOrEmpty(effectiveNamespace)
            ? $"{effectiveNamespace}/{endpointName}"
            : endpointName;
    }

    /// <summary>Flat key: the immediate folder name, no namespace machinery (File endpoints)</summary>
    private static string? BuildFlatKey(string file, EndpointDefinition definition)
    {
        var endpointName = Path.GetFileName(Path.GetDirectoryName(file)) ?? "";
        if (string.IsNullOrWhiteSpace(endpointName))
        {
            Log.Warning("Could not determine endpoint name for {File}", file);
            return null;
        }
        definition.FolderName = endpointName;
        return endpointName;
    }
}
