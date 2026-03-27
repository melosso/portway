namespace PortwayApi.Services.Mcp;

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using PortwayApi.Services;
using Serilog;

/// <summary>
/// Orchestrates a single chat turn: resolves the AI provider, builds tool definitions
/// from the MCP registry, runs the tool-use loop, and writes SSE events to the response.
/// </summary>
public sealed partial class McpChatService
{
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters           = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    private readonly McpEndpointRegistry _registry;
    private readonly ChatOptions         _options;
    private readonly IHttpClientFactory  _httpFactory;
    private readonly SqlMetadataService? _sqlMetadata;

    // Source-generated zero-overhead regexes
    [GeneratedRegex(@"[^a-zA-Z0-9_]")]
    private static partial Regex SanitisePattern();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex ODataIdentifierPattern();

    private static readonly string _systemPromptBase = """
        You are a helpful assistant connected to Portway, a business API gateway.

        LANGUAGE RULE (highest priority):
        Always reply in the exact language the user wrote their message in.
        Determine language by the words the user typed — NOT by the topic they asked about.
        Example: "Tell me about the netherlands" is written in English → reply in English.
        Tool results, field values, or data in any other language do not change your reply language.

        TOOL RULE (always applies):
        Before answering any question — including general knowledge questions — scan the available tools.
        If any tool's name or description is relevant to what the user is asking about, call it first.
        Use real data from the tool to inform your answer rather than relying on general knowledge.
        Only answer from general knowledge if no tool is relevant at all.

        OData query building — the "query" parameter is appended as a query string to the GET URL.
        Field names are case-sensitive: use the exact casing from [fields: ...] in the tool description.
        CRITICAL: When [fields: ...] is listed for a tool, you MUST only use those exact field names in
        $filter, $select, and $orderby. NEVER infer, guess, or use field names not in the list.
        If you are unsure which field to sort or filter on, use $top=N with no $filter or $orderby first,
        then inspect the returned data to find the correct field name before retrying.

        Clauses (combine with &):
        - Limit rows:          $top=20
        - Specific fields:     $select=Name,Code
        - Skip rows:           $skip=20  (use with $top for pagination)
        - Count total:         $count=true
        - Sort ascending:      $orderby=Name asc
        - Sort descending:     $orderby=Quantity desc

        Filter operators ($filter=...):
        - Equals:              Field eq 'Value'        e.g. $filter=Status eq 'Active'
        - Not equals:          Field ne 'Value'        e.g. $filter=Country ne 'NL'
        - Greater than:        Field gt Value          e.g. $filter=QuantityInStock gt 100
        - Greater or equal:    Field ge Value          e.g. $filter=QuantityInStock ge 1
        - Less than:           Field lt Value          e.g. $filter=QuantityInStock lt 10
        - Less or equal:       Field le Value          e.g. $filter=Price le 99.99
        - Boolean/numeric:     Field eq true           e.g. $filter=IsActive eq true
        - Null check:          Field eq null           e.g. $filter=Region ne null
        - Contains text:       contains(Field,'text')  e.g. $filter=contains(Name,'Smith')
        - Starts with:         startswith(Field,'x')   e.g. $filter=startswith(Code,'WH')
        - Ends with:           endswith(Field,'x')     e.g. $filter=endswith(Code,'001')
        - Combine (and):       (A eq 'x') and (B gt 5) e.g. $filter=IsActive eq true and QuantityInStock gt 0
        - Combine (or):        (A eq 'x') or (A eq 'y')

        Examples:
        - "active items with stock":  $filter=IsActive eq true and QuantityInStock gt 0&$top=20
        - "top 5 highest stock":      $orderby=QuantityInStock desc&$top=5
        - "names only":               $select=Name&$top=20
        - "count all active":         $filter=IsActive eq true&$count=true&$top=0
        - "search by name":           $filter=contains(Name,'keyword')&$top=20

        Cross-endpoint queries:
        When the user's question spans multiple tools (e.g. "show stock per warehouse"), call each relevant
        tool separately and combine the results in your response — Portway endpoints do not support joins.

        Rules:
        - ALWAYS scan tools before answering. If any tool name or description matches the topic (even loosely), call it. Do not answer from general knowledge when a relevant tool exists.
        - ALWAYS call a tool when the user asks about data. Never refuse by saying you lack the capability.
        - Always include $top (e.g. $top=20) unless the user explicitly asks for all records.
        - When the user asks for specific fields, use $select with only those fields.
        - When the user asks to filter, search, or find the highest/lowest/most/least, use $filter or $orderby.
        - Use $orderby=Field desc&$top=N for "highest/most" and $orderby=Field asc&$top=N for "lowest/least".
        - If a tool has NO [fields: ...] annotation, call it with $top=5 first (no $filter or $orderby) to
          discover what fields actually exist, then use those real field names for any follow-up queries.
        - If a tool returns an empty result, report "no records found" and STOP.
        - If a tool returns an error, explain it clearly and do not retry unless the user asks.
        - Never call the same tool with the same parameters more than once per turn.
        - Summarise results in plain language — use a short table or list, never dump raw JSON.
        - ALWAYS reply in the same language the user used in their message. The language of data returned
          by tools (e.g. Dutch field values, French descriptions) must never change your response language.
          If the user writes in English, reply in English — even when tool results contain non-English text.

        Non-JSON responses:
        - If a tool result starts with "[Content-Type: text/csv]", the data is CSV. Present it as a markdown table.
        - If a tool result starts with "[Content-Type: text/xml]" or "[Content-Type: application/xml]", summarise the structure in plain language — do not dump raw XML.
        - If a tool result starts with "[Content-Type: text/plain]", present it as readable text.
        - If a tool result starts with "[Binary content:]", tell the user the file cannot be shown in chat and they should download it directly from the Portway API.
        - Never return raw CSV or XML to the user — always convert to a human-readable format.
        """;

    // Minimal JSON Schema for tool inputs — each tool accepts env + optional query/body
    private static readonly string _toolInputSchema = """
        {
          "type": "object",
          "properties": {
            "environment": { "type": "string", "description": "The Portway environment to query (e.g. '500')" },
            "query":       { "type": "string", "description": "OData query string for GET requests (optional). Always use $top to limit results (e.g. '$top=20'). Combine with $filter, $select, $orderby as needed." },
            "body":        { "type": "string", "description": "JSON body for POST/PATCH/PUT requests (optional)" }
          },
          "required": ["environment"]
        }
        """;

    public McpChatService(
        McpEndpointRegistry registry,
        IOptions<ChatOptions> options,
        IHttpClientFactory httpFactory,
        SqlMetadataService? sqlMetadata = null)
    {
        _registry    = registry;
        _options     = options.Value;
        _httpFactory = httpFactory;
        _sqlMetadata = sqlMetadata;
    }

    public bool IsEnabled => _options.Enabled;

    public IReadOnlyList<ToolDefinition> GetToolDefinitions() =>
        _registry.Tools
            .GroupBy(t => (t.Namespace, t.EndpointName))
            .Select(g =>
            {
                var methods = string.Join(", ", g.Select(t => t.Method));
                var first   = g.First();
                var envInfo = first.AllowedEnvironments is { Count: > 0 }
                    ? $" [environment: {string.Join(" or ", first.AllowedEnvironments)}]"
                    : string.Empty;
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
                // Human label: aggregate per-method summaries, e.g. "Retrieve product stock"
                var displayParts = g
                    .Select(t => t.DisplayDescription)
                    .Where(d => !string.IsNullOrWhiteSpace(d))
                    .Distinct()
                    .ToList();
                var displayDesc = displayParts.Count > 0
                    ? string.Join(" / ", displayParts)
                    : first.EndpointName; // fallback: bare endpoint name

                return new ToolDefinition(
                    // Sanitise name for LLM: namespace_endpoint, lowercase, underscores only
                    Name: SanitiseName(string.IsNullOrEmpty(first.Namespace)
                        ? first.EndpointName
                        : $"{first.Namespace}_{first.EndpointName}"),
                    Description: $"{first.Description} (methods: {methods}){fieldInfo}{envInfo}{ctHint}",
                    InputSchema: _toolInputSchema,
                    DisplayDescription: displayDesc
                );
            })
            .ToList();

    /// <summary>
    /// Returns the "Namespace/EndpointName" display keys of SQL tools whose field metadata
    /// could not be resolved. Used by the UI to surface health warnings without re-logging.
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
    /// Runs a complete chat turn with the tool-use loop.
    /// Writes SSE events directly to <paramref name="writer"/>.
    /// Event format: data: {json}\n\n
    /// Types: text | tool_call | done | error
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
        var provider = BuildProvider();
        if (provider is null)
        {
            await WriteSseAsync(writer, new ChatDelta { Type = ChatDeltaType.Error, Delta = "Chat provider not configured. Set Chat:Enabled and Chat:ApiKeyEnvVar in appsettings." }, ct);
            await WriteSseAsync(writer, new ChatDelta { Type = ChatDeltaType.Done }, ct);
            return;
        }

        var tools = GetToolDefinitions();

        // Build a locale-aware system prompt so the LLM formats numbers correctly
        var systemPrompt = BuildSystemPrompt(locale);

        var mutableHistory = new List<ChatMessage> { systemPrompt };
        mutableHistory.AddRange(history);

        // Tool-use loop — LLM may request multiple sequential tool calls
        const int MaxToolRounds = 5;
        for (var round = 0; round < MaxToolRounds; round++)
        {
            // After the first round the client has already seen the initial typing indicator.
            // Signal it to re-show thinking dots while we call the LLM again after tool execution.
            if (round > 0)
                await WriteSseAsync(writer, new ChatDelta { Type = ChatDeltaType.Thinking }, ct);

            var pendingToolCalls = new List<(string name, string input)>();

            await foreach (var delta in provider.StreamAsync(mutableHistory, tools, ct))
            {
                switch (delta.Type)
                {
                    case ChatDeltaType.ToolCall:
                        // Don't stream tool_call to client yet — execute first, then report result
                        pendingToolCalls.Add((delta.ToolName!, delta.ToolInput ?? "{}"));
                        break;

                    case ChatDeltaType.Done:
                        // Provider stream ended — fall through to check pendingToolCalls below.
                        // The final "done" for the client is sent after all tool rounds complete.
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

            // Execute each tool call and feed results back into history
            // Truncate large results to avoid exceeding the model's context window
            const int MaxToolResultChars = 12_000;
            var toolResultParts = new StringBuilder();
            foreach (var (name, inputJson) in pendingToolCalls)
            {
                var result = await ExecuteToolAsync(name, inputJson, defaultEnvironment, baseUrl, authToken, ct);

                var historyResult = result.Length > MaxToolResultChars
                    ? result[..MaxToolResultChars] + $"\n[...truncated: {result.Length - MaxToolResultChars} additional characters omitted. Ask for fewer results or apply a filter.]"
                    : result;
                toolResultParts.AppendLine($"[{name}]: {historyResult}");

                await WriteSseAsync(writer, new ChatDelta
                {
                    Type       = ChatDeltaType.ToolCall,
                    ToolName   = name,
                    ToolInput  = inputJson,
                    ToolResult = result
                }, ct);
            }

            // Append assistant tool-call message + tool results as user message
            mutableHistory.Add(new ChatMessage("assistant", $"I called the following tools:\n{toolResultParts}"));
            mutableHistory.Add(new ChatMessage("user", $"Tool results:\n{toolResultParts}\nPlease continue."));
        }

        await WriteSseAsync(writer, new ChatDelta { Type = ChatDeltaType.Done }, ct);
    }

    private async Task<string> ExecuteToolAsync(
        string toolName,
        string inputJson,
        string defaultEnvironment,
        string baseUrl,
        string? authToken,
        CancellationToken ct)
    {
        // Find the matching registered tool — match by namespaced name (e.g. wms_warehouses)
        var tool = _registry.Tools.FirstOrDefault(t =>
            SanitiseName(string.IsNullOrEmpty(t.Namespace)
                ? t.EndpointName
                : $"{t.Namespace}_{t.EndpointName}").Equals(toolName, StringComparison.OrdinalIgnoreCase));

        if (tool is null)
            return $"Unknown tool: {toolName}";

        JsonNode? input = null;
        try { input = JsonNode.Parse(inputJson); }
        catch (Exception ex) { Log.Debug(ex, "Could not parse tool input JSON for {Tool}", toolName); }

        var environment = input?["environment"]?.GetValue<string>() ?? defaultEnvironment;

        // If the tool has an allowed-environments restriction and the selected env isn't in it,
        // automatically fall back to the first allowed environment so the call succeeds.
        if (tool.AllowedEnvironments is { Count: > 0 } &&
            !tool.AllowedEnvironments.Contains(environment, StringComparer.OrdinalIgnoreCase))
        {
            environment = tool.AllowedEnvironments[0];
        }

        var query  = input?["query"]?.GetValue<string>();
        var body   = input?["body"]?.GetValue<string>();
        var method = new HttpMethod(tool.Method.ToUpperInvariant());

        // Build the endpoint path, including namespace if present
        var endpointPath = string.IsNullOrEmpty(tool.Namespace)
            ? tool.EndpointName
            : $"{tool.Namespace}/{tool.EndpointName}";

        var url = $"{baseUrl}/api/{environment}/{endpointPath}";
        if (!string.IsNullOrEmpty(query))
            url += $"?{query.TrimStart('?')}";

        try
        {
            // Resolve token: caller-forwarded > InternalApiToken config (decrypt if PWENC-encrypted)
            var token = authToken ?? ResolveConfigSecret(_options.InternalApiToken);

            using var http = _httpFactory.CreateClient("internal");
            using var req  = new HttpRequestMessage(method, url);

            if (!string.IsNullOrWhiteSpace(token))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            if (body is not null && method != HttpMethod.Get)
                req.Content = new StringContent(body, Encoding.UTF8, new MediaTypeHeaderValue("application/json"));

            using var resp    = await http.SendAsync(req, ct);
            var       content = await resp.Content.ReadAsStringAsync(ct);

            if (resp.IsSuccessStatusCode)
            {
                var mediaType = resp.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? string.Empty;

                // Block binary types — sending raw bytes to the LLM is wasteful and meaningless
                if (IsBinaryContentType(mediaType))
                    return $"[Binary content: {mediaType}] This endpoint returned a file that cannot be " +
                           $"read in chat. The user should download it directly from the Portway API.";

                // Prefix non-JSON text responses so the LLM knows the format
                if (!string.IsNullOrEmpty(mediaType) && !mediaType.Contains("json"))
                    content = $"[Content-Type: {mediaType}]\n{content}";

                // If the endpoint returned an empty JSON array, give the LLM an explicit signal
                // so it doesn't retry with rephrased queries.
                var trimmed = content.AsSpan().Trim();
                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    try
                    {
                        var node = JsonNode.Parse(content);
                        if (node is JsonArray arr && arr.Count == 0)
                            return "[No records found] The endpoint returned an empty result for this query. Do not retry.";
                    }
                    catch { /* not valid JSON array — return as-is */ }
                }
                return content;
            }

            // Return a clear, actionable message so the LLM can relay it to the user
            var hint = (int)resp.StatusCode switch
            {
                401 => "The request was rejected because no valid Bearer token was provided. " +
                       "The user should authenticate in the Chat UI before querying endpoints.",
                403 => "The Bearer token does not have permission to access this endpoint or environment.",
                404 => $"The endpoint '{endpointPath}' was not found at {url}. " +
                       "Check that the endpoint name and environment are correct.",
                429 => "The request was rate-limited. Try again in a moment.",
                500 => "The endpoint returned an internal server error. Check the Portway logs for details.",
                503 => "The endpoint is unavailable. The upstream service may be down.",
                _   => $"The request failed with HTTP {(int)resp.StatusCode}."
            };

            Log.Warning("Tool call {Tool} → {Status}: {Hint}", toolName, (int)resp.StatusCode, hint);
            return $"[Tool error {(int)resp.StatusCode}] {hint}\nResponse body: {content}";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Tool execution failed for {Tool}", toolName);
            return $"Error executing {toolName}: {ex.Message}";
        }
    }

    private static ChatMessage BuildSystemPrompt(string? locale)
    {
        // Locale is used ONLY for number formatting — not to infer response language.
        // Response language is determined solely by the language the user typed (see LANGUAGE RULE above).
        // Injecting "Always respond in Dutch" here caused the model to switch languages even when
        // the user wrote in English about a Dutch topic (e.g. "Tell me about the Netherlands").
        var localeHint = string.Empty;
        if (!string.IsNullOrWhiteSpace(locale))
        {
            try
            {
                var culture = System.Globalization.CultureInfo.GetCultureInfo(locale);
                var nf      = culture.NumberFormat;
                localeHint = $"\nNumber formatting (locale '{locale}'): " +
                             $"thousands separator '{nf.NumberGroupSeparator}', decimal separator '{nf.NumberDecimalSeparator}'. " +
                             $"Apply this to all numbers in your responses.";
            }
            catch { /* unknown locale — omit hint */ }
        }

        return new ChatMessage("system", _systemPromptBase + localeHint);
    }

    private IChatProvider? BuildProvider()
    {
        if (!_options.Enabled) return null;

        // Priority 1: environment variable
        var apiKey = Environment.GetEnvironmentVariable(_options.ApiKeyEnvVar);

        // Priority 2: Chat:ApiKey in appsettings (may be PWENC-encrypted)
        if (string.IsNullOrWhiteSpace(apiKey) && !string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            var raw = _options.ApiKey;
            if (PortwayApi.Helpers.SettingsEncryptionHelper.IsEncrypted(raw))
            {
                if (!PortwayApi.Helpers.SettingsEncryptionHelper.TryDecryptValue(raw, out var decrypted))
                {
                    Log.Error("Failed to decrypt Chat:ApiKey — check that PORTWAY_ENCRYPTION_KEY is set correctly");
                    return null;
                }
                apiKey = decrypted;
            }
            else
            {
                apiKey = raw;
            }
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Log.Warning("Chat API key not configured. Set the '{EnvVar}' environment variable or Chat:ApiKey in appsettings.", _options.ApiKeyEnvVar);
            return null;
        }

        return _options.Provider.ToLowerInvariant() switch
        {
            "openai"  => new OpenAiChatProvider(apiKey, _options.Model, _httpFactory),
            "gemini"  => new GeminiChatProvider(apiKey, _options.Model, _httpFactory),
            "mistral" => new MistralChatProvider(apiKey, _options.Model, _httpFactory),
            _         => new AnthropicChatProvider(apiKey, _options.Model, _httpFactory)
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
    /// Looks up auto-discovered column names from <see cref="SqlMetadataService"/>.
    /// Only applies to SQL endpoints (EndpointKind == "api" and metadata service available).
    /// Tries "Namespace/EndpointName" first (the cache key used by SqlMetadataService),
    /// then bare "EndpointName" as a fallback for non-namespaced endpoints.
    /// </summary>
    private IReadOnlyList<string>? ResolveFieldsFromMetadata(string? ns, string endpointName, string endpointKind)
    {
        if (_sqlMetadata is null || endpointKind != "api") return null;

        var fullKey = string.IsNullOrEmpty(ns) ? endpointName : $"{ns}/{endpointName}";
        var columns = _sqlMetadata.GetObjectMetadata(fullKey)
                   ?? _sqlMetadata.GetObjectMetadata(endpointName);

        if (columns is { Count: > 0 })
        {
            // Only expose columns whose names are valid OData identifiers (no spaces or special chars).
            // Columns with spaces must be configured via AllowedColumns with alias syntax
            // e.g. "Invoice Due Age;InvoiceDueAge" to be safely usable in $filter/$orderby.
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

        Log.Warning("MCP tool {Key}: no SQL metadata found. This will limit the LLM's functionality to assist." +
                    "Check that the endpoint DB connection succeeded during startup.", fullKey);
        return null;
    }

    private static bool IsBinaryContentType(string mediaType) =>
        mediaType.StartsWith("image/") || mediaType.StartsWith("video/") ||
        mediaType.StartsWith("audio/") ||
        mediaType is "application/pdf" or "application/octet-stream"
                  or "application/zip" or "application/x-zip-compressed";

    /// <summary>Returns the plaintext value of a config secret, decrypting it if PWENC-prefixed.</summary>
    private static string? ResolveConfigSecret(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!PortwayApi.Helpers.SettingsEncryptionHelper.IsEncrypted(value)) return value;
        if (PortwayApi.Helpers.SettingsEncryptionHelper.TryDecryptValue(value, out var plain)) return plain;
        Log.Warning("Failed to decrypt PWENC-encrypted config value — token will be empty");
        return null;
    }
}
