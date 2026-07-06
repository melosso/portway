namespace PortwayApi.Services.Mcp;

using System.Collections.Immutable;

public class McpEndpointRegistry
{
    // ImmutableList allows lock-free reads; replaced atomically on each RegisterEndpoints call
    private volatile ImmutableList<McpToolDescriptor> _tools = ImmutableList<McpToolDescriptor>.Empty;

    // Pre-built O(1) lookup from sanitized tool name to descriptor; rebuilt atomically alongside _tools
    private volatile ImmutableDictionary<string, McpToolDescriptor> _toolByName =
        ImmutableDictionary<string, McpToolDescriptor>.Empty;

    // Cached tool definitions built by McpChatService; stored as object to avoid a circular dependency, invalidated on RegisterEndpoints
    internal volatile object? CachedToolDefinitions;

    /// <summary>Set during startup to re-build and register all MCP endpoints from disk; invoked via <see cref="Refresh"/> after hot-reload</summary>
    public Action? RefreshAction { get; set; }

    /// <summary>Rebuilds the registry from the current endpoint files on disk</summary>
    public void Refresh() => RefreshAction?.Invoke();

    /// <summary>Lock-free snapshot of all registered tool descriptors</summary>
    public ImmutableList<McpToolDescriptor> Tools => _tools;

    /// <summary>O(1) lookup by sanitized tool name; returns null if not found</summary>
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

                // Build the LLM-facing description from the richest source: endpoint description, then method summary, then generic fallback
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

                // Index by sanitized name; first method variant wins for lookup purposes
                var key = SanitiseName(string.IsNullOrEmpty(ep.Namespace)
                    ? ep.Name
                    : $"{ep.Namespace}_{ep.Name}");
                dictBuilder.TryAdd(key, descriptor);
            }
        }

        // Atomic swap; readers see either the old or new snapshot, never a partial state
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
