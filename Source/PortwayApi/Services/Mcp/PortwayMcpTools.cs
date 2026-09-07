namespace PortwayApi.Services.Mcp;

using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

[McpServerToolType]
public static class PortwayMcpTools
{
    private static McpEndpointRegistry?    _registry;

    public static void Initialize(McpEndpointRegistry registry)
    {
        _registry = registry;
    }

    [McpServerTool(ReadOnly = true, Idempotent = true, OpenWorld = false), Description("Browse available Portway endpoints with an interactive UI")]
    public static string ListEndpoints()
    {
        if (_registry is null) return "MCP not initialized";

        var byInvokeName = _registry.ToolsByInvokeName;
        if (byInvokeName.Count == 0) return "No endpoints registered";

        var sb      = new System.Text.StringBuilder();
        var grouped = byInvokeName.GroupBy(kv => kv.Value.Namespace ?? "default");

        sb.AppendLine("# Portway Endpoints\n");
        foreach (var group in grouped)
        {
            sb.AppendLine($"## {group.Key}");
            foreach (var (invokeName, tool) in group)
            {
                sb.AppendLine($"- **{invokeName}**: {tool.Description}");
                if (!string.IsNullOrEmpty(tool.UiResourceUri))
                    sb.AppendLine($"  - Has UI: {tool.UiResourceUri}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    [McpServerTool(ReadOnly = true, Idempotent = true, OpenWorld = false), Description("Get details about a specific endpoint including available methods and URL. Pass the name shown by ListEndpoints")]
    public static EndpointInfoResult GetEndpointInfo(string endpointName)
    {
        if (_registry is null)
            return new EndpointInfoResult { Error = "MCP not initialized" };

        var tool = _registry.FindByName(endpointName);

        if (tool is null)
            return new EndpointInfoResult { Error = $"Endpoint '{endpointName}' not found" };

        return new EndpointInfoResult
        {
            InvokeName = endpointName,
            Name = tool.EndpointName,
            Ns = tool.Namespace,
            Method = tool.Method,
            Url = tool.Url,
            AllowedEnvironments = tool.AllowedEnvironments ?? [],
            HasUi = !string.IsNullOrEmpty(tool.UiResourceUri),
            UiUri = tool.UiResourceUri
        };
    }

    [McpServerTool(ReadOnly = true, Idempotent = true, OpenWorld = false), Description("List endpoints that have MCP Apps UI support")]
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

    [McpServerTool(Destructive = true, OpenWorld = false),
     Description("Call a registered Portway endpoint. Use ListEndpoints/GetEndpointInfo first to find the endpoint name, method and allowed environments.")]
    public static async Task<string> CallEndpoint(
        McpChatService chat,
        IHttpContextAccessor httpContextAccessor,
        [Description("Endpoint name as returned by ListEndpoints, e.g. 'namespace_endpoint' or 'endpoint'")] string endpointName,
        [Description("The Portway environment to call, e.g. '500'")] string environment,
        [Description("OData query string for GET requests (optional), e.g. '$top=20&$filter=...'")] string? query = null,
        [Description("JSON body for POST/PUT/PATCH requests (optional)")] string? body = null,
        CancellationToken ct = default)
    {
        if (_registry is null) return "MCP not initialized";

        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null) return "No active HTTP request context to resolve the base URL and credentials from.";

        // avoid host header injection
        var req        = httpContext.Request;
        var serverPort = req.Host.Port ?? (req.IsHttps ? 443 : 80);
        var baseUrl    = $"{req.Scheme}://localhost:{serverPort}";

        // forward caller bearer token
        var authHeader = req.Headers.Authorization.FirstOrDefault();
        string? bearerToken = authHeader?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true
            ? authHeader["Bearer ".Length..].Trim()
            : null;

        var inputJson = JsonSerializer.Serialize(new { environment, query, body });
        return await chat.ExecuteToolAsync(endpointName, inputJson, environment, baseUrl, bearerToken, ct);
    }
}
