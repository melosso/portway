using System;
using System.Collections.Generic;
using System.Linq;
using Serilog;
using PortwayApi.Classes;

namespace PortwayApi.Helpers;

public static class EndpointSummaryHelper
{
    public static void LogEndpointSummary(
        Dictionary<string, EndpointDefinition> sqlEndpoints,
        Dictionary<string, (string Url, HashSet<string> Methods, bool IsPrivate, string Type)> proxyEndpointMap,
        Dictionary<string, EndpointDefinition> webhookEndpoints,
        Dictionary<string, EndpointDefinition> fileEndpoints)
    {
        var separator = new string('─', 80);
        
        Log.Information(separator);
        Log.Information("📋 Endpoint Configuration Summary");
        Log.Information(separator);
        
        // SQL endpoints
        if (sqlEndpoints.Count > 0)
        {
            Log.Information("📊 SQL Endpoints ({Count})", sqlEndpoints.Count);
            var lastSqlKey = sqlEndpoints.Keys.OrderBy(k => k).Last();
            foreach (var endpoint in sqlEndpoints.OrderBy(e => e.Key))
            {
                string prefix = endpoint.Key == lastSqlKey ? "└──" : "├──";
                Log.Information("│ {Prefix} {Name}: {Schema}.{Object}", 
                    prefix, endpoint.Key, endpoint.Value.DatabaseSchema, endpoint.Value.DatabaseObjectName);
            }
            Log.Information("│");
        }
        
        // Proxy endpoints
        var standardProxyEndpoints = proxyEndpointMap
            .Where(e => !e.Value.IsPrivate && e.Value.Type != "Composite")
            .OrderBy(e => e.Key)
            .ToList();
            
        if (standardProxyEndpoints.Count > 0)
        {
            Log.Information("🌐 Proxy Endpoints ({Count})", standardProxyEndpoints.Count);
            var lastProxyKey = standardProxyEndpoints.Last().Key;
            foreach (var entry in standardProxyEndpoints)
            {
                var (url, methods, _, _) = entry.Value;
                string prefix = entry.Key == lastProxyKey ? "└──" : "├──";
                Log.Information("│ {Prefix} {Name}: {Url} [{Methods}]", 
                    prefix, entry.Key, url, string.Join(", ", methods));
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
            Log.Information("🧩 Composite Endpoints ({Count})", compositeEndpoints.Count);
            var lastCompositeKey = compositeEndpoints.Last().Key;
            foreach (var entry in compositeEndpoints)
            {
                var (url, methods, _, _) = entry.Value;
                string prefix = entry.Key == lastCompositeKey ? "└──" : "├──";
                Log.Information("│ {Prefix} {Name}: {Url} [{Methods}]", 
                    prefix, entry.Key, url, string.Join(", ", methods));
            }
            Log.Information("│");
        }
        
        // Private endpoints
        var privateEndpoints = proxyEndpointMap
            .Where(e => e.Value.IsPrivate)
            .OrderBy(e => e.Key)
            .ToList();
            
        if (privateEndpoints.Count > 0)
        {
            Log.Information("🔒 Private Endpoints ({Count})", privateEndpoints.Count);
            var lastPrivateKey = privateEndpoints.Last().Key;
            foreach (var entry in privateEndpoints)
            {
                var (url, methods, _, _) = entry.Value;
                string prefix = entry.Key == lastPrivateKey ? "└──" : "├──";
                Log.Information("│ {Prefix} {Name}: {Url} [{Methods}]", 
                    prefix, entry.Key, url, string.Join(", ", methods));
            }
            Log.Information("│");
        }
        
        // Webhook endpoints
        if (webhookEndpoints.Count > 0)
        {
            Log.Information("🔔 Webhook Endpoints ({Count})", webhookEndpoints.Count);
            var lastWebhookKey = webhookEndpoints.Keys.OrderBy(k => k).Last();
            
            foreach (var endpoint in webhookEndpoints.OrderBy(e => e.Key))
            {
                string prefix = endpoint.Key == lastWebhookKey ? "└──" : "├──";
                
                // Handle the allowed webhook IDs if they exist
                var allowedIds = endpoint.Value.AllowedColumns != null && endpoint.Value.AllowedColumns.Count > 0
                    ? string.Join(", ", endpoint.Value.AllowedColumns)
                    : "All";
                    
                Log.Information("│ {Prefix} {Name}: [{AllowedIds}]", 
                    prefix, endpoint.Key, allowedIds);
            }
            Log.Information("│");
        }
        
        // File endpoints
        if (fileEndpoints.Count > 0)
        {
            int publicFileCount = fileEndpoints.Count(e => !e.Value.IsPrivate);
            int privateFileCount = fileEndpoints.Count(e => e.Value.IsPrivate);
            
            Log.Information("📁 File Endpoints ({Count})", fileEndpoints.Count);
            
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
        
        // Summary total
        int totalEndpoints = sqlEndpoints.Count + 
                                standardProxyEndpoints.Count + 
                                compositeEndpoints.Count + 
                                webhookEndpoints.Count + 
                                fileEndpoints.Count;
                                
        Log.Information("🔢 Total Endpoints: {Count}", totalEndpoints);
        Log.Information(separator);
    }
}