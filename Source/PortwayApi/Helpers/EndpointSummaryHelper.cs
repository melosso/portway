using System;
using System.Collections.Generic;
using System.Linq;
using Serilog;
using PortwayApi.Classes;

namespace PortwayApi.Helpers;

public static class EndpointSummaryHelper
{
    /// <summary>
    /// Validates endpoint configuration for naming conflicts
    /// </summary>
    public static void ValidateAndLogDuplicateEndpoints(
        Dictionary<string, EndpointDefinition> sqlEndpoints,
        Dictionary<string, (string Url, HashSet<string> Methods, bool IsPrivate, string Type)> proxyEndpointMap,
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
    
    /// <summary>
    /// Adds endpoints to registry while filtering out dual-key duplicates from namespace backward compatibility
    /// </summary>
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
    
    /// <summary>
    /// Adds proxy endpoints to registry with composite filtering
    /// </summary>
    private static void AddUniqueEndpoints(Dictionary<string, List<string>> registry, Dictionary<string, (string Url, HashSet<string> Methods, bool IsPrivate, string Type)> endpoints, string type, bool excludeComposite = false, bool onlyComposite = false)
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

    public static void LogEndpointSummary(
        Dictionary<string, EndpointDefinition> sqlEndpoints,
        Dictionary<string, (string Url, HashSet<string> Methods, bool IsPrivate, string Type)> proxyEndpointMap,
        Dictionary<string, EndpointDefinition> webhookEndpoints,
        Dictionary<string, EndpointDefinition> fileEndpoints,
        Dictionary<string, EndpointDefinition> staticEndpoints)
    {
        // First, validate and log any duplicate endpoint names
        ValidateAndLogDuplicateEndpoints(sqlEndpoints, proxyEndpointMap, webhookEndpoints, fileEndpoints, staticEndpoints);
        
        var separator = new string('─', 80);
        
        Log.Information(separator);
        Log.Information("Endpoint Configuration Summary");
        Log.Information(separator);
        
        // SQL endpoints
        if (sqlEndpoints.Count > 0)
        {
            int publicSqlCount = sqlEndpoints.Count(e => !e.Value.IsPrivate);
            int privateSqlCount = sqlEndpoints.Count(e => e.Value.IsPrivate);
            
            Log.Information("| SQL Endpoints ({Count})", sqlEndpoints.Count);
            
            // Public SQL endpoints
            var publicSqlEndpoints = sqlEndpoints
                .Where(e => !e.Value.IsPrivate)
                .OrderBy(e => e.Key)
                .ToList();
                
            if (publicSqlEndpoints.Count > 0)
            {
                Log.Information("│ ├── Public ({Count})", publicSqlCount);
                var lastPublicKey = publicSqlEndpoints.Last().Key;
                
                foreach (var endpoint in publicSqlEndpoints)
                {
                    string prefix = endpoint.Key == lastPublicKey ? "└──" : "├──";
                    Log.Information("│ │ {Prefix} {Name}: {Schema}.{Object}", 
                        prefix, endpoint.Key, endpoint.Value.DatabaseSchema, endpoint.Value.DatabaseObjectName);
                }
            }
            
            // Private SQL endpoints
            var privateSqlEndpoints = sqlEndpoints
                .Where(e => e.Value.IsPrivate)
                .OrderBy(e => e.Key)
                .ToList();
                
            if (privateSqlEndpoints.Count > 0)
            {
                string privatePrefix = publicSqlCount > 0 ? "└──" : "├──";
                Log.Information("│ {Prefix} Private ({Count})", privatePrefix, privateSqlCount);
                var lastPrivateKey = privateSqlEndpoints.Last().Key;
                
                foreach (var endpoint in privateSqlEndpoints)
                {
                    string prefix = endpoint.Key == lastPrivateKey ? "└──" : "├──";
                    Log.Information("│ │ {Prefix} {Name}: {Schema}.{Object}", 
                        prefix, endpoint.Key, endpoint.Value.DatabaseSchema, endpoint.Value.DatabaseObjectName);
                }
            }
            Log.Information("│");
        }
        
        // Proxy endpoints (both public and private)
        var allProxyEndpoints = proxyEndpointMap
            .Where(e => e.Value.Type != "Composite")
            .ToList();
            
        if (allProxyEndpoints.Count > 0)
        {
            int publicProxyCount = allProxyEndpoints.Count(e => !e.Value.IsPrivate);
            int privateProxyCount = allProxyEndpoints.Count(e => e.Value.IsPrivate);
            
            Log.Information("| Proxy Endpoints ({Count})", allProxyEndpoints.Count);
            
            // Public proxy endpoints
            var publicProxyEndpoints = allProxyEndpoints
                .Where(e => !e.Value.IsPrivate)
                .OrderBy(e => e.Key)
                .ToList();
                
            if (publicProxyEndpoints.Count > 0)
            {
                Log.Information("│ ├── Public ({Count})", publicProxyCount);
                var lastPublicKey = publicProxyEndpoints.Last().Key;
                
                foreach (var entry in publicProxyEndpoints)
                {
                    var (url, methods, _, _) = entry.Value;
                    string prefix = entry.Key == lastPublicKey ? "└──" : "├──";
                    Log.Information("│ │ {Prefix} {Name}: {Url} [{Methods}]", 
                        prefix, entry.Key, url, string.Join(", ", methods));
                }
            }
            
            // Private proxy endpoints
            var privateProxyEndpoints = allProxyEndpoints
                .Where(e => e.Value.IsPrivate)
                .OrderBy(e => e.Key)
                .ToList();
                
            if (privateProxyEndpoints.Count > 0)
            {
                string privatePrefix = publicProxyCount > 0 ? "└──" : "├──";
                Log.Information("│ {Prefix} Private ({Count})", privatePrefix, privateProxyCount);
                var lastPrivateKey = privateProxyEndpoints.Last().Key;
                
                foreach (var entry in privateProxyEndpoints)
                {
                    var (url, methods, _, _) = entry.Value;
                    string prefix = entry.Key == lastPrivateKey ? "└──" : "├──";
                    Log.Information("│ │ {Prefix} {Name}: {Url} [{Methods}]", 
                        prefix, entry.Key, url, string.Join(", ", methods));
                }
            }
            Log.Information("│");
        }
        
        // Composite endpoints
        var compositeEndpoints = proxyEndpointMap
            .Where(e => e.Value.Type == "Composite")
            .OrderBy(e => e.Key)
            .ToList();
            
        if (compositeEndpoints.Count > 0)
        {
            Log.Information("| Composite Endpoints ({Count})", compositeEndpoints.Count);
            Log.Information("│ ├── Public ({Count})", compositeEndpoints.Count);
            var lastCompositeKey = compositeEndpoints.Last().Key;
            foreach (var entry in compositeEndpoints)
            {
                var (url, methods, _, _) = entry.Value;
                string prefix = entry.Key == lastCompositeKey ? "└──" : "├──";
                Log.Information("│ │ {Prefix} {Name}: {Url} [{Methods}]", 
                    prefix, entry.Key, url, string.Join(", ", methods));
            }
            Log.Information("│");
        }
        
        // Webhook endpoints
        if (webhookEndpoints.Count > 0)
        {
            int publicWebhookCount = webhookEndpoints.Count(e => !e.Value.IsPrivate);
            int privateWebhookCount = webhookEndpoints.Count(e => e.Value.IsPrivate);
            
            Log.Information("| Webhook Endpoints ({Count})", webhookEndpoints.Count);
            
            // Public webhook endpoints
            var publicWebhookEndpoints = webhookEndpoints
                .Where(e => !e.Value.IsPrivate)
                .OrderBy(e => e.Key)
                .ToList();
                
            if (publicWebhookEndpoints.Count > 0)
            {
                Log.Information("│ ├── Public ({Count})", publicWebhookCount);
                var lastPublicKey = publicWebhookEndpoints.Last().Key;
                
                foreach (var endpoint in publicWebhookEndpoints)
                {
                    string prefix = endpoint.Key == lastPublicKey ? "└──" : "├──";
                    
                    // Handle the allowed webhook IDs if they exist
                    var allowedIds = endpoint.Value.AllowedColumns != null && endpoint.Value.AllowedColumns.Count > 0
                        ? string.Join(", ", endpoint.Value.AllowedColumns)
                        : "All";
                        
                    Log.Information("│ │ {Prefix} {Name}: [{AllowedIds}]", 
                        prefix, endpoint.Key, allowedIds);
                }
            }
            
            // Private webhook endpoints
            var privateWebhookEndpoints = webhookEndpoints
                .Where(e => e.Value.IsPrivate)
                .OrderBy(e => e.Key)
                .ToList();
                
            if (privateWebhookEndpoints.Count > 0)
            {
                string privatePrefix = publicWebhookCount > 0 ? "└──" : "├──";
                Log.Information("│ {Prefix} Private ({Count})", privatePrefix, privateWebhookCount);
                var lastPrivateKey = privateWebhookEndpoints.Last().Key;
                
                foreach (var endpoint in privateWebhookEndpoints)
                {
                    string prefix = endpoint.Key == lastPrivateKey ? "└──" : "├──";
                    
                    // Handle the allowed webhook IDs if they exist
                    var allowedIds = endpoint.Value.AllowedColumns != null && endpoint.Value.AllowedColumns.Count > 0
                        ? string.Join(", ", endpoint.Value.AllowedColumns)
                        : "All";
                        
                    Log.Information("│ │ {Prefix} {Name}: [{AllowedIds}]", 
                        prefix, endpoint.Key, allowedIds);
                }
            }
            Log.Information("│");
        }
        
        // File endpoints
        if (fileEndpoints.Count > 0)
        {
            int publicFileCount = fileEndpoints.Count(e => !e.Value.IsPrivate);
            int privateFileCount = fileEndpoints.Count(e => e.Value.IsPrivate);
            
            Log.Information("| File Endpoints ({Count})", fileEndpoints.Count);
            
            // Public file endpoints
            var publicFileEndpoints = fileEndpoints
                .Where(e => !e.Value.IsPrivate)
                .OrderBy(e => e.Key)
                .ToList();
                
            if (publicFileEndpoints.Count > 0)
            {
                Log.Information("│ ├── Public ({Count})", publicFileCount);
                var lastPublicKey = publicFileEndpoints.Last().Key;
                
                foreach (var endpoint in publicFileEndpoints)
                {
                    string baseDir = endpoint.Value.Properties != null && 
                                        endpoint.Value.Properties.TryGetValue("BaseDirectory", out var bd) && 
                                        bd is string bdStr && !string.IsNullOrEmpty(bdStr) ? 
                                        bdStr : "";
                                        
                    string prefix = endpoint.Key == lastPublicKey ? "└──" : "├──";
                    Log.Information("│ │ {Prefix} {Name}{BaseDir}", 
                        prefix, 
                        endpoint.Key, 
                        !string.IsNullOrEmpty(baseDir) ? $" [Base: {baseDir}]" : "");
                }
            }
            
            // Private file endpoints
            var privateFileEndpoints = fileEndpoints
                .Where(e => e.Value.IsPrivate)
                .OrderBy(e => e.Key)
                .ToList();
                
            if (privateFileEndpoints.Count > 0)
            {
                string privatePrefix = publicFileCount > 0 ? "└──" : "├──";
                Log.Information("│ {Prefix} Private ({Count})", privatePrefix, privateFileCount);
                var lastPrivateKey = privateFileEndpoints.Last().Key;
                
                foreach (var endpoint in privateFileEndpoints)
                {
                    string baseDir = endpoint.Value.Properties != null && 
                                        endpoint.Value.Properties.TryGetValue("BaseDirectory", out var bd) && 
                                        bd is string bdStr && !string.IsNullOrEmpty(bdStr) ? 
                                        bdStr : "";
                                        
                    string prefix = endpoint.Key == lastPrivateKey ? "└──" : "├──";
                    Log.Information("│ │ {Prefix} {Name}{BaseDir}", 
                        prefix, 
                        endpoint.Key, 
                        !string.IsNullOrEmpty(baseDir) ? $" [Base: {baseDir}]" : "");
                }
            }
            Log.Information("│");
        }
        
        // Static endpoints
        if (staticEndpoints.Count > 0)
        {
            int publicStaticCount = staticEndpoints.Count(e => !e.Value.IsPrivate);
            int privateStaticCount = staticEndpoints.Count(e => e.Value.IsPrivate);
            
            Log.Information("| Static Endpoints ({Count})", staticEndpoints.Count);
            
            // Public static endpoints
            var publicStaticEndpoints = staticEndpoints
                .Where(e => !e.Value.IsPrivate)
                .OrderBy(e => e.Key)
                .ToList();
                
            if (publicStaticEndpoints.Count > 0)
            {
                Log.Information("│ ├── Public ({Count})", publicStaticCount);
                var lastPublicKey = publicStaticEndpoints.Last().Key;
                
                foreach (var endpoint in publicStaticEndpoints)
                {
                    string contentType = endpoint.Value.Properties != null && 
                                        endpoint.Value.Properties.TryGetValue("ContentType", out var ct) && 
                                        ct is string ctStr ? ctStr : "unknown";
                                        
                    string prefix = endpoint.Key == lastPublicKey ? "└──" : "├──";
                    Log.Information("│ │ {Prefix} {Name} [{ContentType}]", 
                        prefix, 
                        endpoint.Key, 
                        contentType);
                }
            }
            
            // Private static endpoints
            var privateStaticEndpoints = staticEndpoints
                .Where(e => e.Value.IsPrivate)
                .OrderBy(e => e.Key)
                .ToList();
                
            if (privateStaticEndpoints.Count > 0)
            {
                string privatePrefix = publicStaticCount > 0 ? "└──" : "├──";
                Log.Information("│ {Prefix} Private ({Count})", privatePrefix, privateStaticCount);
                var lastPrivateKey = privateStaticEndpoints.Last().Key;
                
                foreach (var endpoint in privateStaticEndpoints)
                {
                    string contentType = endpoint.Value.Properties != null && 
                                        endpoint.Value.Properties.TryGetValue("ContentType", out var ct) && 
                                        ct is string ctStr ? ctStr : "unknown";
                                        
                    string prefix = endpoint.Key == lastPrivateKey ? "└──" : "├──";
                    Log.Information("│ │ {Prefix} {Name} [{ContentType}]", 
                        prefix, 
                        endpoint.Key, 
                        contentType);
                }
            }
            Log.Information("│");
        }
        
        // Summary total
        int totalEndpoints = sqlEndpoints.Count + 
                                allProxyEndpoints.Count + 
                                compositeEndpoints.Count + 
                                webhookEndpoints.Count + 
                                fileEndpoints.Count +
                                staticEndpoints.Count;
                                
        Log.Information("Total Endpoints: {Count}", totalEndpoints);
        Log.Information(separator);
    }
}