namespace PortwayApi.Services.Mcp;

using ModelContextProtocol.Server;
using System.ComponentModel;

// Named response types — avoid returning untyped object from MCP tool methods
public sealed record EndpointInfoResult
{
    public string?                Error               { get; init; }
    public string?                Name                { get; init; }
    public string?                Ns                  { get; init; }
    public string?                Method              { get; init; }
    public string?                Url                 { get; init; }
    public IReadOnlyList<string>  AllowedEnvironments { get; init; } = [];
    public bool                   HasUi               { get; init; }
    public string?                UiUri               { get; init; }
}

public sealed record UiEnabledEndpointsResult(
    int Count,
    IReadOnlyList<UiEndpointItem> Endpoints
);

public sealed record UiEndpointItem(string Name, string? UiUri);

[McpServerToolType]
public static class PortwayMcpTools
{
    private static McpEndpointRegistry?    _registry;
    private static McpAppsResourceProvider? _appsProvider;

    public static void Initialize(McpEndpointRegistry registry, McpAppsResourceProvider appsProvider)
    {
        _registry     = registry;
        _appsProvider = appsProvider;
    }

    [McpServerTool, Description("Browse available Portway endpoints with an interactive UI")]
    public static string ListEndpoints()
    {
        if (_registry is null) return "MCP not initialized";

        var tools = _registry.Tools;
        if (tools.Count == 0) return "No endpoints registered";

        var sb      = new System.Text.StringBuilder();
        var grouped = tools.GroupBy(t => t.Namespace ?? "default");

        sb.AppendLine("# Portway Endpoints\n");
        foreach (var group in grouped)
        {
            sb.AppendLine($"## {group.Key}");
            foreach (var tool in group)
            {
                sb.AppendLine($"- **{tool.Name}**: {tool.Description}");
                if (!string.IsNullOrEmpty(tool.UiResourceUri))
                    sb.AppendLine($"  - Has UI: {tool.UiResourceUri}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    [McpServerTool, Description("Get details about a specific endpoint including available methods and URL")]
    public static EndpointInfoResult GetEndpointInfo(string endpointName)
    {
        if (_registry is null)
            return new EndpointInfoResult { Error = "MCP not initialized" };

        var tool = _registry.Tools.FirstOrDefault(t =>
            t.EndpointName.Equals(endpointName, StringComparison.OrdinalIgnoreCase));

        if (tool is null)
            return new EndpointInfoResult { Error = $"Endpoint '{endpointName}' not found" };

        return new EndpointInfoResult
        {
            Name                = tool.EndpointName,
            Ns                  = tool.Namespace,
            Method              = tool.Method,
            Url                 = tool.Url,
            AllowedEnvironments = tool.AllowedEnvironments ?? [],
            HasUi               = !string.IsNullOrEmpty(tool.UiResourceUri),
            UiUri               = tool.UiResourceUri
        };
    }

    [McpServerTool, Description("List endpoints that have MCP Apps UI support")]
    public static UiEnabledEndpointsResult ListUiEnabledEndpoints()
    {
        if (_registry is null)
            return new UiEnabledEndpointsResult(0, []);

        var endpoints = _registry.Tools
            .Where(t => !string.IsNullOrEmpty(t.UiResourceUri))
            .GroupBy(t => t.EndpointName)
            .Select(g => new UiEndpointItem(g.Key, g.First().UiResourceUri))
            .ToList();

        return new UiEnabledEndpointsResult(endpoints.Count, endpoints);
    }
}
