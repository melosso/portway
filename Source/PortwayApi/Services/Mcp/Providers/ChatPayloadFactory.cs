namespace PortwayApi.Services.Mcp.Providers;

using System.Text.Json.Nodes;

/// <summary>
/// Builds the request payload fragments shared by the chat providers
/// </summary>
internal static class ChatPayloadFactory
{
    /// <summary>
    /// System turns joined into one text, for providers with a dedicated system slot
    /// </summary>
    public static string? SystemText(IReadOnlyList<ChatMessage> history)
    {
        var parts = history.Where(m => m.Role == ChatMessage.System).Select(m => m.Content).ToList();
        return parts.Count > 0 ? string.Join("\n\n", parts) : null;
    }

    // OpenAI chat completions, also accepted by Mistral
    public static JsonArray Messages(IReadOnlyList<ChatMessage> history)
        => Build(history, m => m switch
        {
            { Role: ChatMessage.Tool, ResultOf: { } call } => new JsonObject
            {
                ["role"] = "tool",
                ["tool_call_id"] = call.Id,
                ["content"] = m.Content
            },
            { Role: ChatMessage.Assistant, ToolCalls.Count: > 0 } => new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = m.Content.Length > 0 ? m.Content : null,
                ["tool_calls"] = Build(m.ToolCalls, c => new JsonObject
                {
                    ["id"] = c.Id,
                    ["type"] = "function",
                    ["function"] = new JsonObject { ["name"] = c.Name, ["arguments"] = c.Input }
                })
            },
            _ => new JsonObject { ["role"] = m.Role, ["content"] = m.Content }
        });

    // Anthropic takes the system text separately and tool results as user content blocks
    public static JsonArray AnthropicMessages(IReadOnlyList<ChatMessage> history)
        => Merged(history.Where(m => m.Role != ChatMessage.System), m => m.Role == ChatMessage.Assistant ? "assistant" : "user", "content", m => m switch
        {
            { Role: ChatMessage.Tool, ResultOf: { } call } =>
                [new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = call.Id, ["content"] = m.Content }],
            _ => [.. Text(m.Content, c => new JsonObject { ["type"] = "text", ["text"] = c }),
                  .. m.ToolCalls.Select(c => new JsonObject { ["type"] = "tool_use", ["id"] = c.Id, ["name"] = c.Name, ["input"] = ParseObject(c.Input) })]
        });

    // Gemini names the assistant role "model" and takes the system text as systemInstruction
    public static JsonArray GeminiContents(IReadOnlyList<ChatMessage> history)
        => Merged(history.Where(m => m.Role != ChatMessage.System), m => m.Role == ChatMessage.Assistant ? "model" : "user", "parts", m => m switch
        {
            { Role: ChatMessage.Tool, ResultOf: { } call } =>
                [new JsonObject { ["functionResponse"] = new JsonObject { ["name"] = call.Name, ["response"] = new JsonObject { ["result"] = m.Content } } }],
            _ => [.. Text(m.Content, c => new JsonObject { ["text"] = c }),
                  .. m.ToolCalls.Select(GeminiCall)]
        });

    private static JsonObject GeminiCall(ChatToolCall call)
    {
        var part = new JsonObject { ["functionCall"] = new JsonObject { ["name"] = call.Name, ["args"] = ParseObject(call.Input) } };
        if (call.Signature is not null) part["thoughtSignature"] = call.Signature;
        return part;
    }

    private static IEnumerable<JsonObject> Text(string content, Func<string, JsonObject> block)
        => content.Length > 0 ? [block(content)] : [];

    // both vendors reject two adjacent turns of the same role
    private static JsonArray Merged(IEnumerable<ChatMessage> history, Func<ChatMessage, string> role, string partsProperty, Func<ChatMessage, IEnumerable<JsonObject>> parts)
    {
        var turns = new JsonArray();
        foreach (var message in history)
        {
            var name = role(message);
            if (turns.Count == 0 || turns[^1]!["role"]!.GetValue<string>() != name)
                turns.Add(new JsonObject { ["role"] = name, [partsProperty] = new JsonArray() });

            var target = turns[^1]![partsProperty]!.AsArray();
            foreach (var part in parts(message))
                target.Add(part);
        }

        return turns;
    }

    private static JsonObject ParseObject(string json)
    {
        try
        {
            return JsonNode.Parse(json) as JsonObject ?? new JsonObject();
        }
        catch (System.Text.Json.JsonException)
        {
            return new JsonObject();
        }
    }

    // Anthropic and Gemini differ only in the schema property name
    public static JsonArray Declarations(IReadOnlyList<ToolDefinition> tools, string schemaProperty)
        => Build(tools, t => new JsonObject
        {
            ["name"] = t.Name,
            ["description"] = t.Description,
            [schemaProperty] = ParseSchema(t)
        });

    // OpenAI function tools, also accepted by Mistral
    public static JsonArray Functions(IReadOnlyList<ToolDefinition> tools)
        => Build(tools, t => new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = t.Name,
                ["description"] = t.Description,
                ["parameters"] = ParseSchema(t)
            }
        });

    private static JsonArray Build<T>(IReadOnlyList<T> source, Func<T, JsonNode> map)
    {
        var array = new JsonArray();
        foreach (var item in source)
            array.Add(map(item));

        return array;
    }

    private static JsonNode ParseSchema(ToolDefinition tool)
    {
        try
        {
            return JsonNode.Parse(tool.InputSchema) ?? new JsonObject();
        }
        catch (System.Text.Json.JsonException ex)
        {
            Serilog.Log.Warning(ex, "Tool {Tool} has an unparseable input schema, sending an empty schema", tool.Name);
            return new JsonObject();
        }
    }
}
