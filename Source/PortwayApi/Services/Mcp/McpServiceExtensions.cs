namespace PortwayApi.Services.Mcp;

using System.Collections.Immutable;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

public sealed record McpOptions
{
    public bool   Enabled                { get; init; } = false;
    public string Path                   { get; init; } = "/mcp";
    public bool   RequireAuthentication  { get; init; } = true;
    public bool   AppsEnabled            { get; init; } = true;
    /// <summary>Enables the chat/AI feature. Credentials are configured via the setup wizard.</summary>
    public bool   ChatEnabled            { get; init; } = false;
    /// <summary>Rows appended as $top when the LLM omits it. Prevents unbounded table scans.</summary>
    public int    DefaultPageSize        { get; init; } = 50;
    /// <summary>Maximum $top value allowed; clamped if the LLM requests more.</summary>
    public int    MaxPageSize            { get; init; } = 200;
    /// <summary>Timeout in seconds for internal tool-execution HTTP calls. Default 30.</summary>
    public int    ToolTimeoutSeconds     { get; init; } = 30;
    /// <summary>Maximum characters kept from a single tool result before truncation.</summary>
    public int    MaxToolResultChars     { get; init; } = 12_000;
}

public static class McpServiceExtensions
{
    public static IServiceCollection AddMcpServices(this IServiceCollection services, IConfiguration configuration)
    {
        // McpConfigDbContext + McpConfigService are registered unconditionally so that
        // the /ui/mcp/setup page and settings endpoint can read/write config even before
        // Mcp:Enabled is set to true.
        var mcpDbPath = Path.Combine(Directory.GetCurrentDirectory(), "mcp.db");
        services.AddDbContextFactory<McpConfigDbContext>(opts =>
            opts.UseSqlite($"Data Source={mcpDbPath}"));
        services.AddSingleton<McpConfigService>();

        // "mcp" client: external AI provider calls (no fixed timeout — providers handle their own)
        services.AddHttpClient("mcp");

        var mcpEnabled = configuration.GetValue<bool>("Mcp:Enabled", false);
        if (mcpEnabled)
        {
            services.Configure<McpOptions>(configuration.GetSection("Mcp"));
            services.AddSingleton<McpEndpointRegistry>();
            services.AddSingleton<McpAppsResourceProvider>();
            services.AddSingleton<McpChatService>();

            // "internal" client: tool-execution calls back into the Portway API.
            // Timeout is configurable; defaults to 30 s to prevent slow SQL from blocking workers.
            var toolTimeout = configuration.GetValue<int>("Mcp:ToolTimeoutSeconds", 30);
            services.AddHttpClient("internal", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(toolTimeout > 0 ? toolTimeout : 30);
            });

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
    // ImmutableList allows lock-free reads; replaced atomically on each RegisterEndpoints call.
    private volatile ImmutableList<McpToolDescriptor> _tools = ImmutableList<McpToolDescriptor>.Empty;

    // Pre-built O(1) lookup: sanitized tool name → descriptor (first matching method variant).
    // Rebuilt atomically alongside _tools.
    private volatile ImmutableDictionary<string, McpToolDescriptor> _toolByName =
        ImmutableDictionary<string, McpToolDescriptor>.Empty;

    // Cached tool definitions built by McpChatService; invalidated on RegisterEndpoints.
    // Stored as object to avoid a circular dependency on the service type.
    internal volatile object? CachedToolDefinitions;

    /// <summary>
    /// Set during startup to a function that re-builds and registers all MCP endpoints from disk.
    /// Called by <see cref="Refresh"/> so the file watcher can re-populate the registry after hot-reload.
    /// </summary>
    public Action? RefreshAction { get; set; }

    /// <summary>Rebuilds the registry from the current endpoint files on disk.</summary>
    public void Refresh() => RefreshAction?.Invoke();

    /// <summary>Lock-free snapshot of all registered tool descriptors.</summary>
    public ImmutableList<McpToolDescriptor> Tools => _tools;

    /// <summary>O(1) lookup by sanitized tool name. Returns null if not found.</summary>
    public McpToolDescriptor? FindByName(string sanitizedName) =>
        _toolByName.TryGetValue(sanitizedName, out var t) ? t : null;

    public void RegisterEndpoints(IEnumerable<EndpointMcpInfo> endpoints)
    {
        var builder    = ImmutableList.CreateBuilder<McpToolDescriptor>();
        var dictBuilder = ImmutableDictionary.CreateBuilder<string, McpToolDescriptor>(
            StringComparer.OrdinalIgnoreCase);

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

                var descriptor = new McpToolDescriptor
                {
                    Name                = $"{ep.Namespace}_{ep.Name}_{method}".Trim('_'),
                    Description         = llmDescription,
                    DisplayDescription  = humanSummary,
                    EndpointName        = ep.Name,
                    Namespace           = ep.Namespace,
                    Method              = method,
                    AllowedEnvironments = ep.AllowedEnvironments,
                    AvailableFields     = ep.AvailableFields,
                    Url                 = ep.Url,
                    UiResourceUri       = ep.UiEnabled ? $"ui://endpoints/{ep.Name}" : null,
                    ContentType         = ep.ContentType,
                    EndpointKind        = ep.EndpointKind,
                    Instruction         = ep.Instruction
                };

                builder.Add(descriptor);

                // Index by sanitized name; first method variant wins for lookup purposes.
                var key = SanitiseName(string.IsNullOrEmpty(ep.Namespace)
                    ? ep.Name
                    : $"{ep.Namespace}_{ep.Name}");
                dictBuilder.TryAdd(key, descriptor);
            }
        }

        // Atomic swap — readers see either the old or new snapshot, never a partial state.
        _tools              = builder.ToImmutable();
        _toolByName         = dictBuilder.ToImmutable();
        CachedToolDefinitions = null; // invalidate cached ToolDefinition list
    }

    public void Clear()
    {
        _tools                = ImmutableList<McpToolDescriptor>.Empty;
        _toolByName           = ImmutableDictionary<string, McpToolDescriptor>.Empty;
        CachedToolDefinitions = null;
    }

    private static string SanitiseName(string name) =>
        System.Text.RegularExpressions.Regex.Replace(name, @"[^a-zA-Z0-9_]", "_").ToLowerInvariant();
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
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _uiResources = new();

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
