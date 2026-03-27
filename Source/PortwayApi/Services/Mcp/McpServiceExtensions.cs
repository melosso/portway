namespace PortwayApi.Services.Mcp;

using ModelContextProtocol.Server;

public sealed record McpOptions
{
    public bool   Enabled               { get; init; } = false;
    public string Path                  { get; init; } = "/mcp";
    public bool   RequireAuthentication { get; init; } = true;
    public bool   AppsEnabled           { get; init; } = true;
}

public static class McpServiceExtensions
{
    public static IServiceCollection AddMcpServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Chat options and provider HTTP client are registered unconditionally —
        // the chat feature can be enabled independently of the MCP HTTP transport.
        services.Configure<ChatOptions>(configuration.GetSection("Chat"));
        services.AddHttpClient("mcp");

        var mcpEnabled = configuration.GetValue<bool>("Mcp:Enabled", false);
        if (mcpEnabled)
        {
            services.Configure<McpOptions>(configuration.GetSection("Mcp"));
            services.AddSingleton<McpEndpointRegistry>();
            services.AddSingleton<McpAppsResourceProvider>();

            services.AddMcpServer()
                .WithHttpTransport();
        }

        return services;
    }

    public static IEndpointRouteBuilder MapMcpEndpoints(this IEndpointRouteBuilder app, IConfiguration configuration)
    {
        var mcpEnabled = configuration.GetValue<bool>("Mcp:Enabled", false);
        if (mcpEnabled)
        {
            var mcpPath = configuration.GetValue<string>("Mcp:Path") ?? "/mcp";
            app.MapMcp(mcpPath);
        }

        return app;
    }
}

public class McpEndpointRegistry
{
    private readonly List<McpToolDescriptor> _tools = [];
    private readonly object _lock = new();

    /// <summary>
    /// Set during startup to a function that re-builds and registers all MCP endpoints from disk.
    /// Called by <see cref="Refresh"/> so the file watcher can re-populate the registry after hot-reload.
    /// </summary>
    public Action? RefreshAction { get; set; }

    /// <summary>Rebuilds the registry from the current endpoint files on disk.</summary>
    public void Refresh() => RefreshAction?.Invoke();

    public IReadOnlyList<McpToolDescriptor> Tools
    {
        get
        {
            lock (_lock)
            {
                return _tools.ToList().AsReadOnly();
            }
        }
    }

    public void RegisterEndpoints(IEnumerable<EndpointMcpInfo> endpoints)
    {
        lock (_lock)
        {
            _tools.Clear();
            foreach (var ep in endpoints)
            {
                foreach (var method in ep.Methods)
                {
                    var humanSummary = ep.MethodDescriptions?.GetValueOrDefault(method.ToUpperInvariant())
                        ?? ep.MethodDescriptions?.GetValueOrDefault(method)
                        ?? string.Empty;

                    // Build the LLM-facing description from the richest source available:
                    // endpoint-level description → method-level summary → generic fallback.
                    var llmDescription = ep.Description is { Length: > 0 }
                        ? (humanSummary.Length > 0 ? $"{ep.Description} — {humanSummary}" : ep.Description)
                        : (humanSummary.Length > 0 ? humanSummary : $"Call {ep.Name} endpoint with {method} method");

                    if (ep.Instruction is { Length: > 0 })
                        llmDescription = $"{llmDescription}\n\n{ep.Instruction}";

                    _tools.Add(new McpToolDescriptor
                    {
                        Name               = $"{ep.Namespace}_{ep.Name}_{method}".Trim('_'),
                        Description        = llmDescription,
                        DisplayDescription = humanSummary,
                        EndpointName       = ep.Name,
                        Namespace          = ep.Namespace,
                        Method             = method,
                        AllowedEnvironments = ep.AllowedEnvironments,
                        AvailableFields    = ep.AvailableFields,
                        Url                = ep.Url,
                        UiResourceUri      = ep.UiEnabled ? $"ui://endpoints/{ep.Name}" : null,
                        ContentType        = ep.ContentType,
                        EndpointKind       = ep.EndpointKind,
                        Instruction        = ep.Instruction
                    });
                }
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _tools.Clear();
        }
    }
}

public sealed record EndpointMcpInfo
{
    public string                  Name                { get; init; } = string.Empty;
    public string?                 Namespace           { get; init; }
    public string                  Url                 { get; init; } = string.Empty;
    public IReadOnlyList<string>   Methods             { get; init; } = [];
    public IReadOnlyList<string>?  AllowedEnvironments { get; init; }
    public IReadOnlyList<string>?  AvailableFields     { get; init; }
    /// <summary>Human-readable per-method summaries from Documentation.MethodDescriptions.</summary>
    public Dictionary<string, string>? MethodDescriptions { get; init; }
    /// <summary>Endpoint-level description from Documentation.TagDescription or Documentation.Description.</summary>
    public string? Description { get; init; }
    /// <summary>Usage instruction appended to the tool description (from Mcp.Instruction).</summary>
    public string? Instruction { get; init; }
    public bool    UiEnabled    { get; init; } = false;
    /// <summary>MIME type for Static endpoints (e.g. "text/csv"). Null for SQL/Proxy.</summary>
    public string? ContentType  { get; init; }
    /// <summary>"api" | "static" | "file" — affects how the chat layer handles the response.</summary>
    public string  EndpointKind { get; init; } = "api";
}

public sealed record McpToolDescriptor
{
    public string                 Name                { get; init; } = string.Empty;
    /// <summary>LLM-facing description: includes method, fields, and environment metadata.</summary>
    public string                 Description         { get; init; } = string.Empty;
    /// <summary>Human-facing display label, e.g. "Retrieve warehouse locations". Shown in the chat UI.</summary>
    public string                 DisplayDescription  { get; init; } = string.Empty;
    public string                 EndpointName        { get; init; } = string.Empty;
    public string?                Namespace           { get; init; }
    public string                 Method              { get; init; } = string.Empty;
    public IReadOnlyList<string>? AllowedEnvironments { get; init; }
    public IReadOnlyList<string>? AvailableFields     { get; init; }
    public string                 Url                 { get; init; } = string.Empty;
    public string?                UiResourceUri       { get; init; }
    /// <summary>MIME type for Static endpoints (e.g. "text/csv"). Null for SQL/Proxy.</summary>
    public string?                ContentType         { get; init; }
    /// <summary>"api" | "static" | "file" — affects how the chat layer handles the response.</summary>
    public string                 EndpointKind        { get; init; } = "api";
    /// <summary>Usage instruction appended to the tool description (from Mcp.Instruction).</summary>
    public string?                Instruction         { get; init; }
}

public class McpAppsResourceProvider
{
    private readonly Dictionary<string, string> _uiResources = new();

    public void RegisterUiResource(string uri, string htmlContent)
    {
        _uiResources[uri] = htmlContent;
    }

    public string? GetUiResource(string uri)
    {
        return _uiResources.TryGetValue(uri, out var content) ? content : null;
    }

    public IReadOnlyDictionary<string, string> GetAllResources() => _uiResources;

    public static string GenerateEndpointExplorerHtml(IEnumerable<EndpointMcpInfo> endpoints)
    {
        var endpointsList = endpoints.Select(e =>
            $"{{name:\"{EscapeJs(e.Name)}\",ns:\"{EscapeJs(e.Namespace ?? "")}\",url:\"{EscapeJs(e.Url)}\"," +
            $"methods:[{string.Join(",", e.Methods.Select(m => $"\"{EscapeJs(m)}\""  ))}]}}"
        ).ToList();
        var endpointJson = string.Join(",", endpointsList);

        var html = $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Portway Endpoint Explorer</title>
    <style>
        * {{ box-sizing: border-box; margin: 0; padding: 0; }}
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background: #f5f5f5; padding: 20px; line-height: 1.5; }}
        .container {{ max-width: 800px; margin: 0 auto; }}
        h1 {{ color: #1a1a2e; margin-bottom: 8px; }}
        .subtitle {{ color: #666; margin-bottom: 24px; }}
        .endpoint-list {{ display: flex; flex-direction: column; gap: 12px; }}
        .endpoint-card {{ background: white; border-radius: 8px; padding: 16px; box-shadow: 0 1px 3px rgba(0,0,0,0.1); cursor: pointer; transition: box-shadow 0.2s; }}
        .endpoint-card:hover {{ box-shadow: 0 4px 12px rgba(0,0,0,0.15); }}
        .endpoint-header {{ display: flex; align-items: center; gap: 12px; margin-bottom: 8px; flex-wrap: wrap; }}
        .method {{ padding: 2px 8px; border-radius: 4px; font-size: 12px; font-weight: 600; }}
        .method.GET {{ background: #e3f2fd; color: #1565c0; }}
        .method.POST {{ background: #f3e5f5; color: #7b1fa2; }}
        .method.PUT {{ background: #fff3e0; color: #e65100; }}
        .method.DELETE {{ background: #ffebee; color: #c62828; }}
        .endpoint-name {{ font-weight: 600; color: #333; }}
        .endpoint-ns {{ color: #888; font-size: 14px; }}
        .endpoint-url {{ color: #666; font-size: 13px; word-break: break-all; }}
    </style>
</head>
<body>
    <div class=""container"">
        <h1>Portway Endpoint Explorer</h1>
        <p class=""subtitle"">Browse and interact with available endpoints</p>
        <div class=""endpoint-list"" id=""endpoints""></div>
    </div>
    <script>
        var endpoints = [{endpointJson}];
        var container = document.getElementById('endpoints');
        for (var i = 0; i < endpoints.length; i++) {{
            var ep = endpoints[i];
            var card = document.createElement('div');
            card.className = 'endpoint-card';
            var methodsHtml = '';
            for (var j = 0; j < ep.methods.length; j++) {{
                methodsHtml += '<span class=""method ' + ep.methods[j] + '"">' + ep.methods[j] + '</span>';
            }}
            var nsHtml = ep.ns ? '<span class=""endpoint-ns"">(' + ep.ns + ')</span>' : '';
            card.innerHTML = '<div class=""endpoint-header"">' + methodsHtml + '<span class=""endpoint-name"">' + ep.name + '</span>' + nsHtml + '</div><div class=""endpoint-url"">' + ep.url + '</div>';
            var epData = ep;
            card.onclick = (function(e) {{
                return function() {{ window.parent.postMessage({{ type: 'mcp-call-tool', tool: e.name, namespace: e.ns }}, '*'); }};
            }})(epData);
            container.appendChild(card);
        }}
    </script>
</body>
</html>";
        return html;
    }

    private static string EscapeJs(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
}
