using PortwayApi.Classes;
using Serilog;

namespace PortwayApi.Services.Mcp;

/// <summary>Startup wiring for the MCP endpoint registry; builds the tool list, wires hot-reload and maps the MCP Apps route</summary>
public static class McpRegistryStartupExtensions
{
    public static WebApplication MapMcpRegistry(this WebApplication app, string proxyEndpointsDirectory)
    {
        var mcpRegistry     = app.Services.GetRequiredService<McpEndpointRegistry>();
        var mcpAppsProvider = app.Services.GetRequiredService<McpAppsResourceProvider>();

        // Collect all MCP-exposed endpoints
        var mcpEndpoints = new List<EndpointMcpInfo>();

        // Helper to add MCP-exposed endpoints from EndpointDefinition dictionaries
        void AddMcpEndpoints(Dictionary<string, EndpointDefinition> endpoints)
        {
            foreach (var kvp in endpoints)
            {
                if (kvp.Value.IsMcpExposed)
                {
                    var endpointKind = kvp.Value.Type switch {
                        EndpointType.Static => "static",
                        EndpointType.Files  => "file",
                        _                   => "api"
                    };
                    mcpEndpoints.Add(new EndpointMcpInfo
                    {
                        Name = kvp.Value.EndpointName,
                        Namespace = kvp.Value.EffectiveNamespace,
                        Url = kvp.Value.Url,
                        // File endpoints expose GET only; POST (upload) and DELETE are not useful in chat
                        Methods = kvp.Value.Type == EndpointType.Files
                            ? kvp.Value.Methods.Where(m => m.Equals("GET", StringComparison.OrdinalIgnoreCase)).ToList()
                            : kvp.Value.Methods,
                        AllowedEnvironments = kvp.Value.AllowedEnvironments,
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
                    });
                }
            }
        }

        // Helper for GetEndpoints which returns tuples
        void AddMcpEndpointsFromTuple(Dictionary<string, ProxyEndpointInfo> endpoints)
        {
            foreach (var kvp in endpoints)
            {
                if (kvp.Value.IsMcpExposed)
                {
                    mcpEndpoints.Add(new EndpointMcpInfo
                    {
                        Name = kvp.Key,
                        Url = kvp.Value.Url,
                        Methods = kvp.Value.Methods.ToList(),
                        AllowedEnvironments = kvp.Value.AllowedEnvironments
                    });
                }
            }
        }

        // Rebuilds the full MCP endpoint list; stored as a delegate so EndpointFileWatcher can re-run it after hot-reload
        void RefreshMcpRegistry()
        {
            mcpEndpoints.Clear();
            AddMcpEndpoints(EndpointHandler.GetSqlEndpoints());
            AddMcpEndpointsFromTuple(EndpointHandler.GetEndpoints(proxyEndpointsDirectory));
            AddMcpEndpoints(EndpointHandler.GetSqlWebhookEndpoints());
            AddMcpEndpoints(EndpointHandler.GetFileEndpoints());
            AddMcpEndpoints(EndpointHandler.GetStaticEndpoints());
            mcpRegistry.RegisterEndpoints(mcpEndpoints);
            var html = McpAppsResourceProvider.GenerateEndpointExplorerHtml(mcpEndpoints);
            mcpAppsProvider.RegisterUiResource("ui://portway/endpoint-explorer", html);
            Log.Information("MCP registry refreshed: {Count} tools registered", mcpEndpoints.Count);
        }

        AddMcpEndpoints(EndpointHandler.GetSqlEndpoints());
        AddMcpEndpointsFromTuple(EndpointHandler.GetEndpoints(proxyEndpointsDirectory));
        AddMcpEndpoints(EndpointHandler.GetSqlWebhookEndpoints());
        AddMcpEndpoints(EndpointHandler.GetFileEndpoints());
        AddMcpEndpoints(EndpointHandler.GetStaticEndpoints());

        mcpRegistry.RegisterEndpoints(mcpEndpoints);

        // Wire up hot-reload: when endpoint files change, re-build the registry from disk
        mcpRegistry.RefreshAction = RefreshMcpRegistry;

        // Register MCP Apps UI resources
        var explorerHtml = McpAppsResourceProvider.GenerateEndpointExplorerHtml(mcpEndpoints);
        mcpAppsProvider.RegisterUiResource("ui://portway/endpoint-explorer", explorerHtml);

        // Initialize the MCP protocol tools
        PortwayMcpTools.Initialize(mcpRegistry, mcpAppsProvider);

        // Add MCP Apps UI resource endpoint
        app.MapGet("/mcp/apps/{*path}", async (HttpContext context, CancellationToken ct) =>
        {
            var path = context.Request.RouteValues["path"]?.ToString() ?? "";
            var resourceUri = $"ui://portway/{path.TrimStart('/')}";

            if (mcpAppsProvider.GetUiResource(resourceUri) is { } html)
            {
                context.Response.ContentType = "text/html; profile=mcp-app";
                await context.Response.WriteAsync(html, ct);
                return;
            }

            context.Response.StatusCode = 404;
        }).ExcludeFromDescription();

        Log.Information("MCP Apps support enabled, resources registered: {Count}", mcpAppsProvider.GetAllResources().Count);

        return app;
    }
}
