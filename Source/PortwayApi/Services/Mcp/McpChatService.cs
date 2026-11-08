namespace PortwayApi.Services.Mcp;

using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using PortwayApi.Api;
using PortwayApi.Auth;
using PortwayApi.Classes;
using PortwayApi.Services;
using PortwayApi.Services.Mcp.Providers;
using Serilog;

/// <summary>
/// Orchestrates a single chat turn. Resolves the AI provider, builds tool definitions from the MCP registry, runs the tool use loop, and writes SSE events to the response.
/// </summary>
public sealed partial class McpChatService
{
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    private readonly McpEndpointRegistry _registry;
    private readonly McpOptions _mcpOptions;
    private readonly McpConfigService _configService;
    private readonly IHttpClientFactory _httpFactory;
    private readonly SqlMetadataService? _sqlMetadata;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly EnvironmentSettings _environments;

    // Source-generated zero-overhead regexes
    [GeneratedRegex(@"[^a-zA-Z0-9_]")]
    private static partial Regex SanitisePattern();


    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex ODataIdentifierPattern();

    // Minimal JSON Schema for tool inputs; each tool accepts env + optional query/body
    private static readonly string _toolInputSchema = """
        {
          "type": "object",
          "properties": {
            "environment": { "type": "string", "description": "The Portway environment to query (e.g. '500')" },
            "method":      { "type": "string", "description": "HTTP method from the methods listed for this tool (optional). Defaults to the first listed method." },
            "key":         { "type": "string", "description": "Record key, e.g. '42' or 'ABC' (optional)" },
            "path":        { "type": "string", "description": "Path below the endpoint, e.g. a table-valued function parameter such as '3' (optional)" },
            "query":       { "type": "string", "description": "OData query string for GET requests (optional). Always use $top to limit results (e.g. '$top=20'). Combine with $filter, $select, $orderby as needed." },
            "body":        { "type": "string", "description": "JSON body for POST/PATCH/PUT requests (optional)" },
            "tenants":     { "type": "object", "additionalProperties": { "type": "string" }, "description": "Tenant header values for tools that list [tenant headers: ...] (optional when the token holds one value)" }
          },
          "required": ["environment"]
        }
        """;

    public McpChatService(
        McpEndpointRegistry registry,
        IOptions<McpOptions> mcpOptions,
        McpConfigService configService,
        IHttpClientFactory httpFactory,
        IServiceScopeFactory scopeFactory,
        EnvironmentSettings environments,
        SqlMetadataService? sqlMetadata = null)
    {
        _registry = registry;
        _mcpOptions = mcpOptions.Value;
        _configService = configService;
        _httpFactory = httpFactory;
        _scopeFactory = scopeFactory;
        _environments = environments;
        _sqlMetadata = sqlMetadata;
    }

    public bool IsEnabled => _mcpOptions.ChatEnabled;

    /// <summary>
    /// True when a provider and API key are configured in the encrypted DB store.
    /// </summary>
    public async Task<bool> IsConfiguredAsync(CancellationToken ct = default)
    {
        var cfg = await _configService.GetConfigAsync(ct);
        return cfg.IsConfigured;
    }

    /// <summary>
    /// Returns tool definitions, using the registry's cached snapshot when available. The cache is invalidated automatically when <see cref="McpEndpointRegistry.RegisterEndpoints"/> is called
    /// </summary>
    public IReadOnlyList<ToolDefinition> GetToolDefinitions()
    {
        // a reload swaps the snapshot, so a list built from an old one is never served again
        var snapshot = _registry.Current;
        if (snapshot.CachedToolDefinitions is IReadOnlyList<ToolDefinition> cached)
            return cached;

        var tools = BuildToolDefinitions(snapshot.Tools);
        snapshot.CachedToolDefinitions = tools;
        return tools;
    }

    private IReadOnlyList<ToolDefinition> BuildToolDefinitions(ImmutableList<McpToolDescriptor> registered) =>
        registered
            .GroupBy(t => (t.Namespace, t.EndpointName, t.Version))
            .Select(g =>
            {
                var methods = string.Join(", ", g.Select(t => t.Method));
                var first = g.First();
                var envInfo = first.AllowedEnvironments is { Count: > 0 }
                    ? $" [environment: {string.Join(" or ", first.AllowedEnvironments)}]"
                    : string.Empty;
                if (first.TenantHeaders.Count > 0)
                    envInfo += $" [tenant headers: {string.Join(", ", first.TenantHeaders)}]";
                // Prefer explicitly-configured AllowedColumns; fall back to SQL auto-discovered metadata
                var resolvedFields = first.AvailableFields is { Count: > 0 }
                    ? first.AvailableFields
                    : ResolveFieldsFromMetadata(first.Namespace, first.EndpointName, first.EndpointKind);
                var fieldInfo = resolvedFields is { Count: > 0 }
                    ? $" [fields: {string.Join(", ", resolvedFields)}]"
                    : string.Empty;
                var ctHint = first.ContentType is not null && !first.ContentType.Contains("json")
                    ? $" [returns: {first.ContentType}]"
                    : string.Empty;
                var versionInfo = (first.Version is null ? "" : $" [version: {first.Version}]")
                    + (first.Deprecated ? " [deprecated]" : "")
                    + (first.Expandable.Count > 0 ? $" [expand: {string.Join(", ", first.Expandable)}]" : "");
                // Human label: aggregate per-method summaries, e.g. "Retrieve product stock"
                var displayParts = g
                    .Select(t => t.DisplayDescription)
                    .Where(d => !string.IsNullOrWhiteSpace(d))
                    .Distinct()
                    .ToList();
                var displayDesc = displayParts.Count > 0
                    ? string.Join(" / ", displayParts)
                    : first.EndpointName; // fallback: bare endpoint name

                // Derive tool annotations from registered HTTP methods
                var allMethods = g.Select(t => t.Method.ToUpperInvariant()).ToHashSet();
                var isReadOnly = allMethods.All(m => m == "GET");
                var isDestructive = allMethods.Any(m => m is "DELETE" or "POST" or "PUT" or "PATCH");

                return new ToolDefinition(
                    // Sanitise name for LLM: namespace_endpoint, lowercase, underscores only
                    Name: SanitiseName((string.IsNullOrEmpty(first.Namespace)
                        ? first.EndpointName
                        : $"{first.Namespace}_{first.EndpointName}") + (first.Version is null ? "" : $"_{first.Version}")),
                    Description: $"{first.Description} (methods: {methods}){versionInfo}{fieldInfo}{envInfo}{ctHint}",
                    InputSchema: _toolInputSchema,
                    DisplayDescription: displayDesc,
                    ReadOnly: isReadOnly,
                    Destructive: isDestructive
                );
            })
            .ToList();

    /// <summary>
    /// Returns the "Namespace/EndpointName" display keys of SQL tools whose field metadata could not be resolved. Used by the UI to surface health warnings without re-logging
    /// </summary>
    public IReadOnlyList<string> GetMissingMetadataEndpoints()
    {
        if (_sqlMetadata is null) return [];

        return _registry.Tools
            .GroupBy(t => (t.Namespace, t.EndpointName))
            .Where(g =>
            {
                var first = g.First();
                if (first.EndpointKind != "api") return false;
                if (first.AvailableFields is { Count: > 0 }) return false;
                var fullKey = string.IsNullOrEmpty(first.Namespace)
                    ? first.EndpointName
                    : $"{first.Namespace}/{first.EndpointName}";
                var cols = _sqlMetadata.GetObjectMetadata(fullKey)
                        ?? _sqlMetadata.GetObjectMetadata(first.EndpointName);
                return cols is null || cols.Count == 0;
            })
            .Select(g =>
            {
                var first = g.First();
                return string.IsNullOrEmpty(first.Namespace)
                    ? first.EndpointName
                    : $"{first.Namespace}/{first.EndpointName}";
            })
            .ToList();
    }

    /// <summary>
    /// Runs one chat turn with tool use, writing text, tool_call, done and error SSE events
    /// </summary>
    public async Task StreamAsync(
        IReadOnlyList<ChatMessage> history,
        string defaultEnvironment,
        TextWriter writer,
        string baseUrl,
        string? authToken = null,
        string? locale = null,
        CancellationToken ct = default)
    {
        var provider = await BuildProviderAsync(ct);
        if (provider is null)
        {
            await WriteSseAsync(writer, new ChatDelta { Type = ChatDeltaType.Error, Delta = "Chat not configured. Use the MCP Setup page (/_/mcp/setup) to set a provider and API key." }, ct);
            await WriteSseAsync(writer, new ChatDelta { Type = ChatDeltaType.Done }, ct);
            return;
        }

        var tools = GetToolDefinitions();

        // Build a locale-aware system prompt so the LLM formats numbers correctly
        var systemPrompt = new ChatMessage("system", McpChatSystemPrompt.Build(locale));

        var mutableHistory = new List<ChatMessage> { systemPrompt };
        mutableHistory.AddRange(history);

        // Per-turn deduplication: skip re-executing identical (toolName, inputJson) pairs
        var seenToolCalls = new HashSet<(string, string)>();

        // Tool-use loop; LLM may request multiple sequential tool calls
        const int MaxToolRounds = 5;
        for (var round = 0; round < MaxToolRounds; round++)
        {
            if (ct.IsCancellationRequested) break;

            // After the first round, signal the client to re-show the thinking indicator
            if (round > 0)
                await WriteSseAsync(writer, new ChatDelta { Type = ChatDeltaType.Thinking }, ct);

            var pendingToolCalls = new List<(string name, string input)>();

            await foreach (var delta in provider.StreamAsync(mutableHistory, tools, ct))
            {
                switch (delta.Type)
                {
                    case ChatDeltaType.ToolCall:
                        // Collect tool calls; execute after the stream ends for this round
                        pendingToolCalls.Add((delta.ToolName!, delta.ToolInput ?? "{}"));
                        break;

                    case ChatDeltaType.Done:
                        // Provider stream ended; fall through to tool execution below
                        break;

                    case ChatDeltaType.Error:
                        await WriteSseAsync(writer, delta, ct);
                        await WriteSseAsync(writer, new ChatDelta { Type = ChatDeltaType.Done }, ct);
                        return;

                    default:
                        await WriteSseAsync(writer, delta, ct);
                        break;
                }

                if (delta.Type == ChatDeltaType.Done) break;
            }

            if (pendingToolCalls.Count == 0) break;

            // Execute tool calls and feed results back into history, truncated to avoid blowing the model's context window
            var maxChars = _mcpOptions.MaxToolResultChars;
            var toolResultParts = new StringBuilder();
            var assistantParts = new StringBuilder("I called the following tools:\n");

            foreach (var (name, inputJson) in pendingToolCalls)
            {
                // Server-side deduplication: skip identical calls within this turn
                var callKey = (name, inputJson);
                if (!seenToolCalls.Add(callKey))
                {
                    Log.Debug("MCP: skipping duplicate tool call {Tool} with same parameters", name);
                    continue;
                }

                var result = (await ExecuteToolAsync(name, inputJson, defaultEnvironment, baseUrl, authToken, ct)).Text;

                // Truncate for history to stay within context window limits
                var historyResult = result.Length > maxChars
                    ? result[..maxChars] + $"\n[...truncated: {result.Length - maxChars} additional characters omitted. Ask for fewer results or apply a filter.]"
                    : result;

                toolResultParts.AppendLine($"[{name}]: {historyResult}");
                assistantParts.AppendLine($"- {name}({inputJson}) → {historyResult}");

                // Stream full (untruncated) result to the client for display
                await WriteSseAsync(writer, new ChatDelta
                {
                    Type = ChatDeltaType.ToolCall,
                    ToolName = name,
                    ToolInput = inputJson,
                    ToolResult = result
                }, ct);
            }

            // Compress earlier rounds' tool results before appending this turn, to keep history bloat bounded
            if (round >= 2)
                TrimEarlyToolResultsInHistory(mutableHistory, maxChars: 500);

            mutableHistory.Add(new ChatMessage("assistant", assistantParts.ToString()));
            mutableHistory.Add(new ChatMessage("user", "Please continue based on those tool results."));
        }

        await WriteSseAsync(writer, new ChatDelta { Type = ChatDeltaType.Done }, ct);
    }

    /// <summary>
    /// Truncates the content of tool-result history messages from early rounds to keep the total history size manageable across many tool-use rounds
    /// </summary>
    private static void TrimEarlyToolResultsInHistory(List<ChatMessage> history, int maxChars)
    {
        // Compress only messages before the last 4 entries (current + previous round pairs); the system prompt is never touched
        var trimBefore = history.Count - 4;
        for (var i = 1; i < trimBefore; i++)
        {
            var msg = history[i];
            if (msg.Role == "assistant" && msg.Content.StartsWith("I called", StringComparison.Ordinal)
                && msg.Content.Length > maxChars)
            {
                history[i] = msg with
                {
                    Content = msg.Content[..maxChars] + "\n[...earlier tool results compressed]"
                };
            }
        }
    }

    // internal, reused by mcp tool call
    internal async Task<McpToolResult> ExecuteToolAsync(
        string toolName,
        string inputJson,
        string defaultEnvironment,
        string baseUrl,
        string? authToken,
        CancellationToken ct)
    {
        // Tool execution must run under the caller's own API token
        if (string.IsNullOrWhiteSpace(authToken))
            return McpToolResult.Failure("Tool execution requires an API token. Attach an Authorization: Bearer header to this chat request.");

        var tool = _registry.FindByName(toolName);

        // scope failure reads as missing
        if (tool is null || !await CallerHasAccessAsync(authToken, tool))
            return McpToolResult.Failure($"Unknown tool: {toolName}");

        JsonObject? input = null;
        try { input = JsonNode.Parse(inputJson) as JsonObject; }
        catch (Exception ex) { Log.Debug(ex, "Could not parse tool input JSON for {Tool}", toolName); }

        var environment = ReadArgument(input, "environment") ?? defaultEnvironment;

        if (tool.AllowedEnvironments is { Count: > 0 } toolEnvironments &&
            !toolEnvironments.Contains(environment, StringComparer.OrdinalIgnoreCase))
        {
            return McpToolResult.Failure($"Environment '{environment}' is not allowed for {toolName}. Allowed environments: {string.Join(", ", toolEnvironments)}.");
        }

        if (!_environments.IsEnvironmentAllowed(environment))
            return McpToolResult.Failure($"Environment '{environment}' is not allowed.");

        var methodName = ReadArgument(input, "method") ?? tool.Method;
        var methods = _registry.MethodsOf(tool);
        if (!methods.Contains(methodName, StringComparer.OrdinalIgnoreCase))
            return McpToolResult.Failure($"Method '{methodName}' is not available on {toolName}. Available methods: {string.Join(", ", methods)}.");

        var method = new HttpMethod(methodName.ToUpperInvariant());
        var isQueryMethod = method.Method == "QUERY";
        var query = ReadArgument(input, "query");
        var body = ReadArgument(input, "body");

        if (method == HttpMethod.Get && !string.IsNullOrEmpty(body))
            return McpToolResult.Failure($"A GET request has no body. Set method to one of: {string.Join(", ", methods)}.");

        if (BuildRoutePath(tool, method, ReadArgument(input, "key"), ReadArgument(input, "path")) is not { } route)
            return McpToolResult.Failure($"The key or path argument is not valid for {toolName}.");

        if (isQueryMethod)
            body = tool.IsProxy
                ? (string.IsNullOrWhiteSpace(body) ? "{}" : body)
                : EnforceQueryBodyLimit(body, _mcpOptions.DefaultPageSize, _mcpOptions.MaxPageSize);
        else if (method == HttpMethod.Get && tool.EndpointKind != "file")
            query = EnforceTopLimit(query ?? string.Empty, _mcpOptions.DefaultPageSize, _mcpOptions.MaxPageSize);

        var endpointPath = tool.RoutePath;
        var url = $"{baseUrl}/api/{Uri.EscapeDataString(environment)}/{route}";
        if (!string.IsNullOrEmpty(query))
            url += $"?{RemoveFragment(query).TrimStart('?')}";

        var sw = Stopwatch.StartNew();
        try
        {
            using var http = _httpFactory.CreateClient("internal");
            using var req = new HttpRequestMessage(method, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
            if (input?["tenants"] is JsonObject tenants)
            {
                foreach (var header in tool.TenantHeaders)
                {
                    if (tenants[header] is JsonValue value && value.TryGetValue<string>(out var tenant) && Auth.TenantGrants.IsValidValue(tenant))
                        req.Headers.TryAddWithoutValidation(header, tenant);
                }
            }

            if (body is not null && method != HttpMethod.Get)
                req.Content = new StringContent(body, Encoding.UTF8, new MediaTypeHeaderValue("application/json"));

            // ResponseHeadersRead lets a large response abort early instead of buffering the full body before the MaxToolResultChars check
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            sw.Stop();

            Log.Information("MCP tool {Tool} → {Status} in {Elapsed}ms ({Path})",
                toolName, (int)resp.StatusCode, sw.ElapsedMilliseconds, route);

            if (resp.IsSuccessStatusCode)
            {
                var mediaType = resp.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? string.Empty;

                // Block binary types; sending raw bytes to the LLM is wasteful and meaningless
                if (IsBinaryContentType(mediaType))
                    return new McpToolResult($"[Binary content: {mediaType}] This endpoint returned a file that cannot be " +
                           $"read in chat. The user should download it directly from the Portway API.");

                // Size-capped read: stream only up to MaxToolResultChars plus a small buffer, then discard the rest, so huge responses never load fully into memory
                var maxChars = _mcpOptions.MaxToolResultChars;
                var content = await ReadCappedAsync(resp.Content, maxChars + 1, ct);
                var truncated = content.Length > maxChars;
                if (truncated)
                    content = content[..(char.IsHighSurrogate(content[maxChars - 1]) ? maxChars - 1 : maxChars)];

                // Prefix non-JSON text responses so the LLM knows the format
                if (!string.IsNullOrEmpty(mediaType) && !mediaType.Contains("json"))
                    content = $"[Content-Type: {mediaType}]\n{content}";

                // If the endpoint returned an empty JSON array, give the LLM an explicit signal so it doesn't retry with rephrased queries
                var trimmed = content.AsSpan().Trim();
                if (trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[^1] == ']' && trimmed[1..^1].IsWhiteSpace())
                    return new McpToolResult("[No records found] The endpoint returned an empty result for this query. Do not retry.");

                return new McpToolResult(truncated ? DescribeTruncation(content, resp.Content.Headers.ContentLength) : content);
            }

            // Return a clear, actionable message so the LLM can relay it to the user
            var errorBody = await ReadCappedAsync(resp.Content, 2000, ct);
            var hint = (int)resp.StatusCode switch
            {
                301 => $"The endpoint '{endpointPath}' has moved permanently to a new URL. Update the target endpoint path.",
                302 or 307 or 308 => $"The endpoint '{endpointPath}' redirected to a different location. Check if the request URI or protocol should be updated.",

                400 when errorBody.Contains("validation", StringComparison.OrdinalIgnoreCase) =>
                    $"The request to '{endpointPath}' failed validation. Check the payload field requirements.",
                400 => $"The request was malformed or missing required parameters for '{endpointPath}'. Check the payload and query parameters.",

                401 when errorBody.Contains("expired", StringComparison.OrdinalIgnoreCase) =>
                    "The Bearer token has expired. The user must re-authenticate in the Chat UI to get a fresh token.",
                401 => "The request was rejected because no valid Bearer token was provided. The user should authenticate in the Chat UI before querying endpoints.",

                403 => "The Bearer token does not have permission to access this endpoint or environment.",
                404 => $"The endpoint '{endpointPath}' was not found. Check that the endpoint name and environment are correct.",
                405 => $"The HTTP method used for '{endpointPath}' is not supported. Verify whether GET, POST, PUT, or DELETE is expected.",
                407 => "Proxy authentication is required before accessing this endpoint.",
                408 => "The client request timed out before the server could receive it. Try re-sending the request.",
                409 => "The request conflicts with the current state of the target resource. Check if the resource already exists or is locked.",
                410 => $"The resource at '{endpointPath}' is permanently gone and will not be available again.",
                412 => "One or more preconditions specified in the request headers evaluated to false.",
                413 => "The request payload is too large for the server to process. Reduce the size of the request body.",
                415 => "The server refuses to accept the request because the payload format is unsupported. Check the Content-Type header (e.g., application/json).",
                422 => "The request was well-formed but contains unprocessable semantic or validation errors. Check field validation details.",
                426 => "The client must upgrade to a different protocol (e.g., TLS/HTTP2) to proceed.",
                429 => "The request was rate-limited. Try again in a moment.",
                431 => "The request header fields are too large to process. Clear unnecessary headers or cookies.",
                451 => "Access to this resource is unavailable due to legal or compliance restrictions.",

                500 => "The endpoint returned an internal server error. Check the Portway logs for details.",
                502 => "The server received an invalid response from an upstream server or gateway.",
                503 => "The endpoint is unavailable. The upstream service may be down or under maintenance.",
                504 => "The server acting as a gateway timed out waiting for an upstream response. Try again shortly.",

                // Fallback
                _ => $"The request failed with HTTP {(int)resp.StatusCode}."
            };

            Log.Warning("Tool call {Tool} → {Status} in {Elapsed}ms: {Hint}",
                toolName, (int)resp.StatusCode, sw.ElapsedMilliseconds, hint);
            return McpToolResult.Failure($"[Tool error {(int)resp.StatusCode}] {hint}\nResponse body: {errorBody}");
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            return McpToolResult.Failure($"[Tool cancelled] The request for {toolName} was cancelled.");
        }
        catch (TaskCanceledException)
        {
            Log.Warning("Tool call {Tool} timed out after {Elapsed}ms", toolName, sw.ElapsedMilliseconds);
            return McpToolResult.Failure($"[Tool timeout] {toolName} did not respond within the configured timeout. Try a more specific query with $filter or $top.");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Tool execution failed for {Tool}", toolName);
            return McpToolResult.Failure($"Error executing {toolName}. Check the Portway logs for details.");
        }
    }

    /// <summary>
    /// Reads response content up to <paramref name="maxChars"/> characters. Uses streaming to avoid buffering a gigantic response before checking its size
    /// </summary>
    private static async Task<string> ReadCappedAsync(HttpContent content, int maxChars, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var buffer = new char[4096];
        var sb = new StringBuilder(Math.Min(maxChars, 65536));

        while (sb.Length < maxChars)
        {
            var remaining = maxChars - sb.Length;
            var toRead = Math.Min(buffer.Length, remaining);
            var read = await reader.ReadAsync(buffer.AsMemory(0, toRead), ct);
            if (read == 0) break;
            sb.Append(buffer, 0, read);
        }

        return sb.ToString();
    }

    private static string? ReadArgument(JsonObject? input, string name) => input?[name] switch
    {
        null => null,
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        var node => node.ToJsonString()
    };

    private async Task<bool> CallerHasAccessAsync(string authToken, McpToolDescriptor tool)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var caller = await scope.ServiceProvider.GetRequiredService<TokenService>().GetTokenDetailsByTokenAsync(authToken);
        return caller is not null && caller.HasAccessToEndpoint(tool.Identity);
    }

    /// <summary>
    /// Path below /api/{env} for a tool call, escaped; null when the key or path is refused
    /// </summary>
    internal static string? BuildRoutePath(McpToolDescriptor tool, HttpMethod method, string? key, string? path)
    {
        var hasKey = !string.IsNullOrEmpty(key);
        var hasPath = !string.IsNullOrEmpty(path);

        if (tool.EndpointKind != "api")
            return hasKey || hasPath ? null : tool.EndpointKind == "file" && method == HttpMethod.Get ? $"{tool.RoutePath}/list" : tool.RoutePath;

        var route = tool.RoutePath;
        if (hasKey)
        {
            if (!IsSafeSegment(key!) || key!.AsSpan().ContainsAny("'()"))
                return null;

            var isBare = key.All(char.IsAsciiDigit) || Guid.TryParse(key, out _);
            route += isBare ? $"({key})" : $"('{Uri.EscapeDataString(key)}')";
        }

        if (hasPath)
        {
            var segments = path!.Trim('/').Split('/');
            if (!segments.All(IsSafeSegment))
                return null;

            // path must stay on endpoint
            string[] full = [.. tool.RoutePath.Split('/'), .. segments];
            if (EndpointController.ResolveEndpointIdentity(full) is not { } resolved)
                return null;

            var identity = resolved.Namespace.Length > 0 ? $"{resolved.Namespace}/{resolved.Name}" : resolved.Name;
            if (!identity.Equals(tool.Identity, StringComparison.OrdinalIgnoreCase))
                return null;

            route += "/" + string.Join('/', segments.Select(Uri.EscapeDataString));
        }

        return route;
    }

    private static bool IsSafeSegment(string segment) =>
        segment.Length > 0 && segment is not ("." or "..") && !segment.Any(c => c is '/' or '\\' || char.IsControl(c));

    private static string RemoveFragment(string query) =>
        query.IndexOf('#') is >= 0 and var hash ? query[..hash] : query;

    /// <summary>
    /// Returns the query with one $top within the page limit, whatever form the caller encoded it in
    /// </summary>
    internal static string EnforceTopLimit(string query, int defaultPageSize, int maxPageSize)
    {
        int? requested = null;
        var parts = new List<string>();

        foreach (var part in RemoveFragment(query).TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            var name = Uri.UnescapeDataString(separator < 0 ? part : part[..separator]).Trim();
            if (!name.Equals("$top", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add(part);
                continue;
            }

            var value = separator < 0 ? string.Empty : Uri.UnescapeDataString(part[(separator + 1)..]);
            if (requested is null && int.TryParse(value, System.Globalization.NumberStyles.None, null, out var top))
                requested = top;
        }

        parts.Add($"$top={Math.Min(requested ?? defaultPageSize, maxPageSize)}");
        return string.Join('&', parts);
    }

    /// <summary>
    /// Returns a QUERY body with $top within the page limit. A body that is not a JSON object passes through: the endpoint answers 400
    /// </summary>
    internal static string EnforceQueryBodyLimit(string? body, int defaultPageSize, int maxPageSize)
    {
        JsonNode? parsed;
        try { parsed = JsonNode.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body); }
        catch (JsonException) { return body!; }

        if (parsed is not JsonObject query)
            return body!;

        var requested = ReadTop(query, "$top") ?? ReadTop(query, "top");
        query.Remove("top");
        query["$top"] = Math.Min(requested ?? defaultPageSize, maxPageSize);
        return query.ToJsonString();

        static int? ReadTop(JsonObject source, string name) =>
            source[name] is JsonValue value && value.TryGetValue<int>(out var top) && top >= 0 ? top : null;
    }

    private static string DescribeTruncation(string partial, long? totalBytes) =>
        JsonSerializer.Serialize(new
        {
            truncated = true,
            returnedChars = partial.Length,
            totalBytes,
            message = "Result truncated. Ask for fewer results or apply a filter.",
            partial
        }, _jsonOpts);

    private async Task<IChatProvider?> BuildProviderAsync(CancellationToken ct)
    {
        if (!_mcpOptions.ChatEnabled) return null;

        var cfg = await _configService.GetConfigAsync(ct);

        if (!cfg.IsConfigured)
        {
            Log.Warning("MCP chat is not configured — use the /_/mcp/setup page to set provider and API key");
            return null;
        }

        var apiKey = cfg.ApiKey!;
        var model = string.IsNullOrWhiteSpace(cfg.Model) ? "claude-sonnet-4-6" : cfg.Model;
        var provider = cfg.Provider;

        return provider.ToLowerInvariant() switch
        {
            "openai" => new OpenAiCompatibleChatProvider("OpenAI", apiKey, model, OpenAiCompatibleChatProvider.OpenAiUrl, _httpFactory),
            "gemini" => new GeminiChatProvider(apiKey, model, _httpFactory),
            "mistral" => new OpenAiCompatibleChatProvider("Mistral", apiKey, model, OpenAiCompatibleChatProvider.MistralUrlFor(model), _httpFactory),
            _ => new AnthropicChatProvider(apiKey, model, _httpFactory)
        };
    }

    private static async Task WriteSseAsync(TextWriter writer, ChatDelta delta, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(delta, _jsonOpts);
        await writer.WriteAsync($"data: {json}\n\n");
        await writer.FlushAsync(ct);
    }

    private static string SanitiseName(string name) =>
        SanitisePattern().Replace(name, "_").ToLowerInvariant();

    /// <summary>
    /// Looks up auto-discovered column names from <see cref="SqlMetadataService"/>. Only applies to SQL endpoints (EndpointKind == "api" and metadata service available)
    /// </summary>
    private IReadOnlyList<string>? ResolveFieldsFromMetadata(string? ns, string endpointName, string endpointKind)
    {
        if (_sqlMetadata is null || endpointKind != "api") return null;

        var fullKey = string.IsNullOrEmpty(ns) ? endpointName : $"{ns}/{endpointName}";
        var columns = _sqlMetadata.GetObjectMetadata(fullKey)
                   ?? _sqlMetadata.GetObjectMetadata(endpointName);

        if (columns is { Count: > 0 })
        {
            var safe = columns
                .Select(c => c.ColumnName)
                .Where(n => ODataIdentifierPattern().IsMatch(n))
                .ToList();

            var skipped = columns.Count - safe.Count;
            if (skipped > 0)
                Log.Debug("MCP tool {Key}: {Skipped} column(s) skipped (names contain spaces/special chars, add AllowedColumns aliases to expose them)", fullKey, skipped);

            Log.Debug("MCP tool {Key}: resolved {Count} safe fields from SQL metadata", fullKey, safe.Count);
            return safe.Count > 0 ? safe : null;
        }

        Log.Warning("MCP tool {Key}: no SQL metadata found. LLM functionality may be limited.", fullKey);
        return null;
    }

    private static bool IsBinaryContentType(string mediaType) =>
        mediaType.StartsWith("image/") || mediaType.StartsWith("video/") ||
        mediaType.StartsWith("audio/") ||
        mediaType is "application/pdf" or "application/octet-stream"
                  or "application/zip" or "application/x-zip-compressed";

}
