using System;
using System.Collections.Generic;
using System.Linq;
using Serilog;
using PortwayApi.Classes;

namespace PortwayApi.Helpers;

public static class EndpointSummaryHelper
{
    /// <summary>Validates endpoint configuration for naming conflicts</summary>
    public static void ValidateAndLogDuplicateEndpoints(
        Dictionary<string, EndpointDefinition> sqlEndpoints,
        Dictionary<string, ProxyEndpointInfo> proxyEndpointMap,
        Dictionary<string, EndpointDefinition> webhookEndpoints,
        Dictionary<string, EndpointDefinition> fileEndpoints,
        Dictionary<string, EndpointDefinition> staticEndpoints)
    {
        // Use a registry that tracks endpoint identities to avoid dual-key false positives
        var endpointRegistry = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        
        // Only add primary endpoint keys to avoid dual-key conflicts
        AddUniqueEndpoints(endpointRegistry, sqlEndpoints, "SQL");
        AddUniqueEndpoints(endpointRegistry, proxyEndpointMap.ToDictionary(x => x.Key, x => x.Value), "Proxy", excludeComposite: true);
        AddUniqueEndpoints(endpointRegistry, proxyEndpointMap.ToDictionary(x => x.Key, x => x.Value), "Composite", onlyComposite: true);
        AddUniqueEndpoints(endpointRegistry, webhookEndpoints, "Webhook");
        AddUniqueEndpoints(endpointRegistry, fileEndpoints, "File");
        AddUniqueEndpoints(endpointRegistry, staticEndpoints, "Static");
        
        var conflicts = endpointRegistry.Where(e => e.Value.Count > 1).ToList();
        
        if (conflicts.Any())
        {
            Log.Warning("Configuration conflict: multiple endpoints share the same path. Please assign unique paths to guarantee consistent routing:");
            
            foreach (var conflict in conflicts.OrderBy(c => c.Key))
            {
                var activeType = GetActiveEndpointType(conflict.Value);
                var shadowedTypes = conflict.Value.Where(t => t != activeType);
                
                Log.Warning("🚧 Endpoint path '{Path}' defined in [{Types}] - {Active} active, {Shadowed} unreachable.", 
                    conflict.Key, 
                    string.Join(", ", conflict.Value),
                    activeType,
                    string.Join(", ", shadowedTypes));
            }
            
            Log.Information(" Note: Endpoints in different namespaces (e.g., 'CRM/Accounts' vs 'Finance/Accounts') do not conflict.");
        }
    }
    
    private static void AddEndpoints(Dictionary<string, List<string>> registry, IEnumerable<string> endpoints, string type)
    {
        foreach (var endpoint in endpoints)
        {
            if (!registry.ContainsKey(endpoint))
                registry[endpoint] = new List<string>();
            registry[endpoint].Add(type);
        }
    }
    
    /// <summary>Adds endpoints to registry while filtering out dual-key duplicates from namespace backward compatibility</summary>
    private static void AddUniqueEndpoints(Dictionary<string, List<string>> registry, Dictionary<string, EndpointDefinition> endpoints, string type)
    {
        foreach (var kvp in endpoints)
        {
            var key = kvp.Key;
            
            // Skip if this looks like a backward-compatibility duplicate
            // (non-namespaced key when a namespaced version exists)
            if (!key.Contains('/'))
            {
                var namespacedVersion = endpoints.Keys.FirstOrDefault(k => k.Contains('/') && k.EndsWith($"/{key}"));
                if (namespacedVersion != null)
                {
                    // This is a backward-compatibility key, skip it to avoid false conflicts
                    continue;
                }
            }
            
            if (!registry.ContainsKey(key))
                registry[key] = new List<string>();
            registry[key].Add(type);
        }
    }
    
    /// <summary>Adds proxy endpoints to registry with composite filtering</summary>
    private static void AddUniqueEndpoints(Dictionary<string, List<string>> registry, Dictionary<string, ProxyEndpointInfo> endpoints, string type, bool excludeComposite = false, bool onlyComposite = false)
    {
        foreach (var kvp in endpoints)
        {
            var key = kvp.Key;
            var endpoint = kvp.Value;
            
            // Apply composite filtering
            if (excludeComposite && endpoint.Type == "Composite") continue;
            if (onlyComposite && endpoint.Type != "Composite") continue;
            
            // Skip if this looks like a backward-compatibility duplicate
            if (!key.Contains('/'))
            {
                var namespacedVersion = endpoints.Keys.FirstOrDefault(k => k.Contains('/') && k.EndsWith($"/{key}"));
                if (namespacedVersion != null)
                {
                    continue;
                }
            }
            
            if (!registry.ContainsKey(key))
                registry[key] = new List<string>();
            registry[key].Add(type);
        }
    }
    
    private static string GetActiveEndpointType(List<string> types)
    {
        // Matches routing precedence in EndpointController.ParseEndpoint
        if (types.Contains("SQL")) return "SQL";
        if (types.Contains("Proxy")) return "Proxy";
        if (types.Contains("File")) return "File";
        if (types.Contains("Static")) return "Static";
        if (types.Contains("Composite")) return "Composite";
        if (types.Contains("Webhook")) return "Webhook";
        return types.First();
    }

    private sealed record SummarySection(string Title, List<(string Group, List<string> Items)> Groups)
    {
        public int Count => Groups.Sum(g => g.Items.Count);
    }

    public static void LogEndpointSummary(
        Dictionary<string, EndpointDefinition> sqlEndpoints,
        Dictionary<string, ProxyEndpointInfo> proxyEndpointMap,
        Dictionary<string, EndpointDefinition> webhookEndpoints,
        Dictionary<string, EndpointDefinition> fileEndpoints,
        Dictionary<string, EndpointDefinition> staticEndpoints)
    {
        ValidateAndLogDuplicateEndpoints(sqlEndpoints, proxyEndpointMap, webhookEndpoints, fileEndpoints, staticEndpoints);

        var proxies = proxyEndpointMap.Where(e => e.Value.Type != "Composite").ToDictionary(e => e.Key, e => e.Value);
        var composites = proxyEndpointMap.Where(e => e.Value.Type == "Composite").ToDictionary(e => e.Key, e => e.Value);

        var sections = new List<SummarySection>();
        AddSection(sections, "SQL", sqlEndpoints, e => $"{e.Key}: {e.Value.DatabaseSchema}.{e.Value.DatabaseObjectName}", e => e.Value.IsPrivate);
        AddSection(sections, "Proxy", proxies, e => $"{e.Key}: {e.Value.Url} [{string.Join(", ", e.Value.Methods)}]", e => e.Value.IsPrivate);
        AddSection(sections, "Composite", composites, e => $"{e.Key}: {e.Value.Url} [{string.Join(", ", e.Value.Methods)}]", e => e.Value.IsPrivate);
        AddSection(sections, "Webhook", webhookEndpoints, e => $"{e.Key}: [{FormatWebhookIds(e.Value)}]", e => e.Value.IsPrivate);
        AddSection(sections, "File", fileEndpoints, e => $"{e.Key}{FormatBaseDir(e.Value)}", e => e.Value.IsPrivate);
        AddSection(sections, "Static", staticEndpoints, e => $"{e.Key} [{FormatContentType(e.Value)}]", e => e.Value.IsPrivate);

        // Rendered as one log event so sinks cannot interleave the tree with other startup lines
        Log.Information("Endpoint configuration ({Total} endpoints){Tree}", sections.Sum(s => s.Count), RenderTree(sections));
    }

    private static void AddSection<T>(List<SummarySection> sections, string title,
        Dictionary<string, T> endpoints, Func<KeyValuePair<string, T>, string> format,
        Func<KeyValuePair<string, T>, bool> isPrivate)
    {
        if (endpoints.Count == 0) return;

        var groups = new List<(string, List<string>)>();
        foreach (var (name, filter) in new (string, bool)[] { ("Public", false), ("Private", true) })
        {
            var items = endpoints.Where(e => isPrivate(e) == filter).OrderBy(e => e.Key)
                .Select(format).ToList();
            if (items.Count > 0) groups.Add((name, items));
        }
        sections.Add(new SummarySection(title, groups));
    }

    private static string RenderTree(List<SummarySection> sections)
    {
        var sb = new System.Text.StringBuilder();
        for (int s = 0; s < sections.Count; s++)
        {
            var section = sections[s];
            bool lastSection = s == sections.Count - 1;
            string pad = lastSection ? "   " : "│  ";
            sb.Append('\n').Append(lastSection ? "└─ " : "├─ ").Append($"{section.Title} ({section.Count})");

            for (int g = 0; g < section.Groups.Count; g++)
            {
                var (group, items) = section.Groups[g];
                bool lastGroup = g == section.Groups.Count - 1;
                string gpad = lastGroup ? "   " : "│  ";
                sb.Append('\n').Append(pad).Append(lastGroup ? "└─ " : "├─ ").Append($"{group} ({items.Count})");

                for (int i = 0; i < items.Count; i++)
                    sb.Append('\n').Append(pad).Append(gpad).Append(i == items.Count - 1 ? "└─ " : "├─ ").Append(items[i]);
            }
        }
        return sb.ToString();
    }

    private static string FormatWebhookIds(EndpointDefinition e) =>
        e.AllowedColumns is { Count: > 0 } ? string.Join(", ", e.AllowedColumns) : "All";

    private static string FormatBaseDir(EndpointDefinition e) =>
        e.Properties != null && e.Properties.TryGetValue("BaseDirectory", out var bd) && bd is string s && s.Length > 0
            ? $" [Base: {s}]" : "";

    private static string FormatContentType(EndpointDefinition e) =>
        e.Properties != null && e.Properties.TryGetValue("ContentType", out var ct) && ct is string s ? s : "unknown";

}