using PortwayApi.Classes;
using Serilog;

namespace PortwayApi.Services.Mcp;

/// <summary>
/// Startup wiring for the MCP endpoint registry; builds the tool list and wires hot-reload
/// </summary>
public static class McpRegistryStartupExtensions
{
    public static WebApplication MapMcpRegistry(this WebApplication app)
    {
        var mcpRegistry = app.Services.GetRequiredService<McpEndpointRegistry>();

        var mcpEndpoints = new List<EndpointMcpInfo>();

        void RefreshMcpRegistry()
        {
            mcpEndpoints.Clear();
            mcpEndpoints.AddRange(BuildEndpointMcpInfos(EndpointHandler.GetSqlEndpoints()));
            mcpEndpoints.AddRange(BuildEndpointMcpInfos(EndpointHandler.GetProxyEndpoints()));
            mcpEndpoints.AddRange(BuildEndpointMcpInfos(EndpointHandler.GetSqlWebhookEndpoints()));
            mcpEndpoints.AddRange(BuildEndpointMcpInfos(EndpointHandler.GetFileEndpoints()));
            mcpEndpoints.AddRange(BuildEndpointMcpInfos(EndpointHandler.GetStaticEndpoints()));
            mcpRegistry.RegisterEndpoints(mcpEndpoints);
            Log.Information("MCP registry refreshed: {Count} tools registered", mcpEndpoints.Count);
        }

        RefreshMcpRegistry();

        // Wire up hot-reload: when endpoint files change, re-build the registry from disk
        mcpRegistry.RefreshAction = RefreshMcpRegistry;

        // Initialize the MCP protocol tools
        PortwayMcpTools.Initialize(mcpRegistry);


        return app;
    }

    internal static IEnumerable<EndpointMcpInfo> BuildEndpointMcpInfos(Dictionary<string, EndpointDefinition> endpoints)
    {
        foreach (var kvp in endpoints)
        {
            if (!kvp.Value.IsMcpExposed || !kvp.Value.Enabled) continue;

            var endpointKind = kvp.Value.Type switch
            {
                EndpointType.Static => "static",
                EndpointType.Files => "file",
                _ => "api"
            };

            yield return new EndpointMcpInfo
            {
                Name = kvp.Value.EndpointName,
                Namespace = kvp.Value.EffectiveNamespace,
                Version = EndpointVersion.IsDefault(kvp.Value.Version) ? null : kvp.Value.Version,
                Url = kvp.Value.Url,
                // File endpoints expose GET only; POST (upload) and DELETE are not useful in chat
                Methods = kvp.Value.Type == EndpointType.Files
                    ? kvp.Value.Methods.Where(m => m.Equals("GET", StringComparison.OrdinalIgnoreCase)).ToList()
                    : kvp.Value.Methods,
                AllowedEnvironments = kvp.Value.AllowedEnvironments,
                TenantHeaders = kvp.Value.Tenancy?.Keys.ToList() ?? [],
                MethodDescriptions = kvp.Value.Documentation?.MethodDescriptions,
                Description = kvp.Value.Documentation?.TagDescription ?? kvp.Value.Documentation?.Description,
                AvailableFields = kvp.Value.AllowedColumns is { Count: > 0 }
                    // Strip Portway alias syntax (e.g. "ItemCode;ProductNumber" → "ProductNumber")
                    ? kvp.Value.AllowedColumns
                        .Select(c => c.Contains(';') ? c[(c.IndexOf(';') + 1)..] : c)
                        .ToList()
                    : null,
                ContentType = kvp.Value.Properties?.TryGetValue("ContentType", out var ct) == true
                    ? ct?.ToString() : null,
                EndpointKind = endpointKind,
                Instruction = kvp.Value.Mcp?.Instruction
            };
        }
    }
}
