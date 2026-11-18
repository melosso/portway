using System.Collections.Frozen;
using PortwayApi.Helpers;
using Serilog;
using System.Text.Json;

namespace PortwayApi.Classes;

/// <summary>
/// Single directory-scan loader shared by the Proxy, SQL, Static and File endpoint types
/// </summary>
internal static class EndpointDirectoryLoader
{
    /// <summary>
    /// Scans the folder; a file that is not valid JSON keeps its definition from previous so a save in progress keeps the endpoint live
    /// </summary>
    public static FrozenDictionary<string, EndpointDefinition> Load(string endpointsDirectory, EndpointLoaderSpec spec, IReadOnlyDictionary<string, EndpointDefinition>? previous = null) =>
        Scan(endpointsDirectory, spec, previous).ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, EndpointDefinition> Scan(string endpointsDirectory, EndpointLoaderSpec spec, IReadOnlyDictionary<string, EndpointDefinition>? previous)
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
                    var (key, definition, errors, metadataErrors) = Evaluate(file, File.ReadAllText(file), endpointsDirectory, spec);
                    if (metadataErrors.Count > 0)
                        Log.Warning($"Ignoring Stability and Badges of {spec.FailPrefix}endpoint {{File}}: {{Errors}}", file, string.Join(" ", metadataErrors));
                    if (key is null || definition is null || errors.Count > 0)
                    {
                        if (errors.Count > 0)
                            Log.Error($"Failed to load {spec.FailPrefix}endpoint from {{File}}: {{Errors}}", file, string.Join("; ", errors));
                        continue;
                    }

                    endpoints[key] = definition;
                    spec.LogLoaded(key, definition);
                }
                catch (JsonException ex)
                {
                    var directory = Path.GetDirectoryName(file);
                    var kept = previous?.Where(kv => kv.Value.ConfigDirectory == directory).ToList() ?? [];
                    foreach (var (key, definition) in kept)
                        endpoints[key] = definition;
                    Log.Warning($"Invalid JSON in {spec.LowerLabel} endpoint {{File}}: {{Message}}. Last valid definition kept: {{Kept}}", file, ex.Message, kept.Count > 0);
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

    internal static (string? Key, EndpointDefinition? Definition, List<string> Errors, IReadOnlyList<string> MetadataErrors) Evaluate(string? file, string json, string endpointsDirectory, EndpointLoaderSpec spec)
    {
        var errors = new List<string>();
        EndpointDefinition? definition;
        try
        {
            definition = spec.Parse(json);
        }
        catch (InvalidOperationException ex)
        {
            errors.Add(ex.Message);
            return (null, null, errors, []);
        }
        catch (ArgumentException)
        {
            errors.Add("Tenancy lists the same header twice");
            return (null, null, errors, []);
        }

        if (definition is null || !spec.IsValid(definition))
        {
            errors.Add(spec.Required);
            return (null, null, errors, []);
        }

        var tenancyErrors = TenancyRules.Validate(definition);
        if (tenancyErrors.Count > 0)
        {
            errors.AddRange(tenancyErrors);
            return (null, definition, errors, []);
        }

        // labels never stop an endpoint
        var metadataErrors = EndpointMetadataRules.Validate(definition);
        if (metadataErrors.Count > 0)
        {
            definition.Stability = null;
            definition.Badges = null;
        }

        if (file is null)
            return (definition.Identity, definition, errors, metadataErrors);

        var key = spec.NamespaceAware
            ? BuildNamespacedKey(file, endpointsDirectory, definition, errors)
            : BuildFlatKey(file, definition, errors);
        if (key is null)
            return (null, definition, errors, metadataErrors);

        if (definition.Verification is { Count: > 0 } && !key.Contains('/'))
        {
            errors.Add("Verification requires a namespace");
            return (null, definition, errors, metadataErrors);
        }

        definition.ConfigDirectory = Path.GetDirectoryName(file);
        return (key, definition, errors, metadataErrors);
    }

    /// <summary>
    /// Namespace-aware key: folder inference, explicit-namespace warnings, validation, "{ns}/{name}" routing key
    /// </summary>
    private static string? BuildNamespacedKey(string file, string endpointsDirectory, EndpointDefinition definition, List<string> errors)
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
            errors.Add("The v1 folder beside this file defines the endpoint");
            return null;
        }

        // Inferred namespace is a fallback; entity.json Namespace takes precedence
        definition.InferredNamespace = inferredNamespace;

        // the folder name is the endpoint name
        definition.FolderName = endpointName;

        if (string.IsNullOrWhiteSpace(endpointName))
        {
            errors.Add("The endpoint name could not be determined from the folder");
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
            errors.AddRange(validationErrors);
            return null;
        }

        if (EndpointVersion.TryParse(definition.EffectiveNamespace?.Split('/')[0], out _))
        {
            Log.Warning("Endpoint {EndpointName}: namespace '{Namespace}' starts with a version segment; a versioned endpoint with the same path takes precedence", endpointName, definition.EffectiveNamespace);
        }

        return definition.Identity;
    }

    // a v1 folder contains the v1 endpoint and takes precedence over the entity.json beside it
    private static bool ShadowedByV1Folder(string file)
    {
        if (!File.Exists(Path.Combine(Path.GetDirectoryName(file)!, EndpointVersion.Default, "entity.json")))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Flat key: the immediate folder name, no namespace machinery (File endpoints)
    /// </summary>
    private static string? BuildFlatKey(string file, EndpointDefinition definition, List<string> errors)
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
            errors.Add("The v1 folder beside this file defines the endpoint");
            return null;
        }

        if (string.IsNullOrWhiteSpace(endpointName))
        {
            errors.Add("The endpoint name could not be determined from the folder");
            return null;
        }
        definition.FolderName = endpointName;
        return endpointName + EndpointVersion.Suffix(definition.Version);
    }
}
