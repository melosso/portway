namespace PortwayApi.Services.Mcp;

using System.Collections.Immutable;

public class McpEndpointRegistry
{
    /// <summary>
    /// Tools, name index and the chat tool-definition cache, swapped as one reference so readers never mix two registrations
    /// </summary>
    internal sealed class Snapshot(ImmutableList<McpToolDescriptor> tools, ImmutableDictionary<string, McpToolDescriptor> byName)
    {
        public ImmutableList<McpToolDescriptor> Tools { get; } = tools;
        public ImmutableDictionary<string, McpToolDescriptor> ByName { get; } = byName;

        // built by McpChatService; stored as object to avoid a circular dependency
        public volatile object? CachedToolDefinitions;
    }

    private volatile Snapshot _current = Empty();

    private static Snapshot Empty() =>
        new(ImmutableList<McpToolDescriptor>.Empty, ImmutableDictionary<string, McpToolDescriptor>.Empty);

    internal Snapshot Current => _current;

    /// <summary>
    /// Set during startup to re-build and register all MCP endpoints from disk; invoked via <see cref="Refresh"/> after hot-reload
    /// </summary>
    public Action? RefreshAction { get; set; }

    /// <summary>
    /// Rebuilds the registry from the current endpoint files on disk
    /// </summary>
    public void Refresh() => RefreshAction?.Invoke();

    /// <summary>
    /// Lock-free snapshot of all registered tool descriptors
    /// </summary>
    public ImmutableList<McpToolDescriptor> Tools => _current.Tools;

    /// <summary>
    /// O(1) lookup by sanitized tool name; returns null if not found
    /// </summary>
    public McpToolDescriptor? FindByName(string sanitizedName) =>
        _current.ByName.TryGetValue(sanitizedName, out var t) ? t : null;

    // same map findbyname uses
    public ImmutableDictionary<string, McpToolDescriptor> ToolsByInvokeName => _current.ByName;

    public void RegisterEndpoints(IEnumerable<EndpointMcpInfo> endpoints)
    {
        var builder = ImmutableList.CreateBuilder<McpToolDescriptor>();
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
                    Name = $"{ep.Namespace}_{ep.Name}_{ep.Version}_{method}".Replace("__", "_").Trim('_'),
                    Description = llmDescription,
                    DisplayDescription = humanSummary,
                    EndpointName = ep.Name,
                    Namespace = ep.Namespace,
                    Version = ep.Version,
                    Method = method,
                    AllowedEnvironments = ep.AllowedEnvironments,
                    TenantHeaders = ep.TenantHeaders,
                    AvailableFields = ep.AvailableFields,
                    Url = ep.Url,
                    ContentType = ep.ContentType,
                    EndpointKind = ep.EndpointKind,
                    Instruction = ep.Instruction
                };

                builder.Add(descriptor);

                // Index by sanitized name; first method variant wins for lookup purposes
                var key = SanitiseName((string.IsNullOrEmpty(ep.Namespace)
                    ? ep.Name
                    : $"{ep.Namespace}_{ep.Name}") + (ep.Version is null ? "" : $"_{ep.Version}"));
                dictBuilder.TryAdd(key, descriptor);
            }
        }

        _current = new Snapshot(builder.ToImmutable(), dictBuilder.ToImmutable());
    }

    public void Clear()
    {
        _current = Empty();
    }

    private static string SanitiseName(string name) =>
        System.Text.RegularExpressions.Regex.Replace(name, @"[^a-zA-Z0-9_]", "_").ToLowerInvariant();
}
