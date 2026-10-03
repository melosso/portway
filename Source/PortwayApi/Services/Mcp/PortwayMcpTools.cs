namespace PortwayApi.Services.Mcp;

using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;
using PortwayApi.Auth;
using System.ComponentModel;
using System.Text.Json;

[McpServerToolType]
public static class PortwayMcpTools
{
    private static McpEndpointRegistry? _registry;

    public static void Initialize(McpEndpointRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>
    /// Same bearer extraction CallEndpoint uses, reused so every tool checks the caller's real token
    /// </summary>
    private static async Task<AuthToken?> ResolveCallerTokenAsync(IHttpContextAccessor httpContextAccessor, TokenService tokenService)
    {
        var authHeader = httpContextAccessor.HttpContext?.Request.Headers.Authorization.FirstOrDefault();
        var token = authHeader?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true
            ? authHeader["Bearer ".Length..].Trim()
            : null;

        return token is null ? null : await tokenService.GetTokenDetailsByTokenAsync(token);
    }

    /// <summary>
    /// Same key TokenAuthMiddleware checks scope against, so listings match what the token can actually call
    /// </summary>
    private static string ScopeKey(McpToolDescriptor tool) =>
        string.IsNullOrEmpty(tool.Namespace) ? tool.EndpointName : $"{tool.Namespace}/{tool.EndpointName}";

    [McpServerTool(ReadOnly = true, Idempotent = true, OpenWorld = false), Description("Browse available Portway endpoints with an interactive UI")]
    public static async Task<string> ListEndpoints(IHttpContextAccessor httpContextAccessor, TokenService tokenService)
    {
        if (_registry is null) return "MCP not initialized";

        var caller = await ResolveCallerTokenAsync(httpContextAccessor, tokenService);
        if (caller is null) return "No endpoints registered";

        var byInvokeName = _registry.ToolsByInvokeName
            .Where(kv => caller.HasAccessToEndpoint(ScopeKey(kv.Value)))
            .ToList();
        if (byInvokeName.Count == 0) return "No endpoints registered";

        var sb = new System.Text.StringBuilder();
        var grouped = byInvokeName.GroupBy(kv => kv.Value.Namespace ?? "default");

        sb.AppendLine("# Portway Endpoints\n");
        foreach (var group in grouped)
        {
            sb.AppendLine($"## {group.Key}");
            foreach (var (invokeName, tool) in group)
            {
                sb.AppendLine($"- **{invokeName}**: {tool.Description}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    [McpServerTool(ReadOnly = true, Idempotent = true, OpenWorld = false), Description("Get details about a specific endpoint including available methods and URL. Pass the name shown by ListEndpoints")]
    public static async Task<EndpointInfoResult> GetEndpointInfo(
        IHttpContextAccessor httpContextAccessor, TokenService tokenService, string endpointName)
    {
        if (_registry is null)
            return new EndpointInfoResult { Error = "MCP not initialized" };

        var tool = _registry.FindByName(endpointName);

        if (tool is null)
            return new EndpointInfoResult { Error = $"Endpoint '{endpointName}' not found" };

        var caller = await ResolveCallerTokenAsync(httpContextAccessor, tokenService);
        if (caller is null || !caller.HasAccessToEndpoint(ScopeKey(tool)))
            return new EndpointInfoResult { Error = $"Endpoint '{endpointName}' not found" };

        return new EndpointInfoResult
        {
            InvokeName = endpointName,
            Name = tool.EndpointName,
            Ns = tool.Namespace,
            Method = tool.Method,
            Url = tool.Url,
            AllowedEnvironments = tool.AllowedEnvironments ?? [],
            TenantHeaders = tool.TenantHeaders
        };
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
        [Description("Tenant header values for endpoints with TenantHeaders in GetEndpointInfo, e.g. {\"X-Company-Id\":\"ACME\"} (optional when the token holds one value)")] Dictionary<string, string>? tenants = null,
        CancellationToken ct = default)
    {
        if (_registry is null) return "MCP not initialized";

        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null) return "No active HTTP request context to resolve the base URL and credentials from.";

        // avoid host header injection
        var req = httpContext.Request;
        var serverPort = req.Host.Port ?? (req.IsHttps ? 443 : 80);
        var baseUrl = $"{req.Scheme}://localhost:{serverPort}";

        // forward caller bearer token
        var authHeader = req.Headers.Authorization.FirstOrDefault();
        string? bearerToken = authHeader?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true
            ? authHeader["Bearer ".Length..].Trim()
            : null;

        var inputJson = JsonSerializer.Serialize(new { environment, query, body, tenants });
        return await chat.ExecuteToolAsync(endpointName, inputJson, environment, baseUrl, bearerToken, ct);
    }
}
