namespace PortwayApi.Services.Mcp;

using ModelContextProtocol.Server;

public sealed record McpOptions
{
    public bool Enabled { get; init; } = false;
    public string Path { get; init; } = "/mcp";
    public bool RequireAuthentication { get; init; } = true;
    public bool AppsEnabled { get; init; } = true;
}

public static class McpServiceExtensions
{
    public static IServiceCollection AddMcpServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Chat options and provider HTTP client are registered unconditionally so the chat feature can be enabled independently of the MCP HTTP transport.
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

    public static string GenerateEndpointExplorerHtml(IEnumerable<EndpointMcpInfo> endpoints) =>
        EndpointExplorerHtml.Generate(endpoints);
}
