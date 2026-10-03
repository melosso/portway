using PortwayApi.Helpers;
using Serilog;
using System.Text.Json;

namespace PortwayApi.Classes;

/// <summary>
/// Single directory-scan loader shared by the Proxy, SQL, Static and File endpoint types
/// </summary>
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

                    var tenancyErrors = TenancyRules.Validate(definition);
                    if (tenancyErrors.Count > 0)
                    {
                        Log.Warning($"Failed to load {spec.FailPrefix}endpoint from {{File}}: {{Errors}}", file, string.Join("; ", tenancyErrors));
                        continue;
                    }

                    var key = spec.NamespaceAware
                        ? BuildNamespacedKey(file, endpointsDirectory, definition)
                        : BuildFlatKey(file, definition);
                    if (key == null) continue;

                    definition.ConfigDirectory = Path.GetDirectoryName(file);
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

    /// <summary>
    /// Namespace-aware key: folder inference, explicit-namespace warnings, validation, "{ns}/{name}" routing key
    /// </summary>
    private static string? BuildNamespacedKey(string file, string endpointsDirectory, EndpointDefinition definition)
    {
        var (inferredNamespace, endpointName) = DirectoryHelper.ExtractNamespaceAndEndpoint(file, endpointsDirectory);

        if (inferredNamespace is not null && EndpointVersion.TryParse(endpointName, out var version))
        {
            definition.Version = version;
            var cut = inferredNamespace.LastIndexOf('/');
            endpointName = inferredNamespace[(cut + 1)..];
            inferredNamespace = cut < 0 ? null : inferredNamespace[..cut];
        }
        else if (ShadowedByV1Folder(file))
        {
            return null;
        }

        // Inferred namespace is a fallback; entity.json Namespace takes precedence
        definition.InferredNamespace = inferredNamespace;

        // the folder name is the endpoint name
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

        if (EndpointVersion.TryParse(definition.EffectiveNamespace?.Split('/')[0], out _))
        {
            Log.Warning("Endpoint {EndpointName}: namespace '{Namespace}' starts with a version segment; a versioned endpoint with the same path takes precedence", endpointName, definition.EffectiveNamespace);
        }

        return definition.Identity;
    }

    // a v1 folder holds the v1 endpoint and takes precedence over the entity.json beside it
    private static bool ShadowedByV1Folder(string file)
    {
        if (!File.Exists(Path.Combine(Path.GetDirectoryName(file)!, EndpointVersion.Default, "entity.json")))
        {
            return false;
        }

        Log.Warning("Skipped {File}: the v1 folder beside it defines this endpoint", file);
        return true;
    }

    /// <summary>
    /// Flat key: the immediate folder name, no namespace machinery (File endpoints)
    /// </summary>
    private static string? BuildFlatKey(string file, EndpointDefinition definition)
    {
        var directory = Path.GetDirectoryName(file);
        var endpointName = Path.GetFileName(directory) ?? "";
        if (EndpointVersion.TryParse(endpointName, out var version))
        {
            definition.Version = version;
            endpointName = Path.GetFileName(Path.GetDirectoryName(directory)) ?? "";
        }

        else if (ShadowedByV1Folder(file))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(endpointName))
        {
            Log.Warning("Could not determine endpoint name for {File}", file);
            return null;
        }
        definition.FolderName = endpointName;
        return endpointName + EndpointVersion.Suffix(definition.Version);
    }
}
