namespace PortwayApi.Services.Mcp.Providers;

using System.Text.Json.Nodes;

/// <summary>Builds the request payload fragments shared by the chat providers</summary>
internal static class ChatPayloadFactory
{
    public static JsonArray Messages(IReadOnlyList<ChatMessage> history)
        => Build(history, m => new JsonObject
        {
            ["role"]    = m.Role,
            ["content"] = m.Content
        });

    // Gemini names the assistant role "model"
    public static JsonArray GeminiContents(IReadOnlyList<ChatMessage> history)
        => Build(history, m => new JsonObject
        {
            ["role"]  = m.Role == "assistant" ? "model" : "user",
            ["parts"] = new JsonArray(new JsonObject { ["text"] = m.Content })
        });

    // Anthropic and Gemini differ only in the schema property name
    public static JsonArray Declarations(IReadOnlyList<ToolDefinition> tools, string schemaProperty)
        => Build(tools, t => new JsonObject
        {
            ["name"]         = t.Name,
            ["description"]  = t.Description,
            [schemaProperty] = ParseSchema(t)
        });

    // OpenAI function tools, also accepted by Mistral
    public static JsonArray Functions(IReadOnlyList<ToolDefinition> tools)
        => Build(tools, t => new JsonObject
        {
            ["type"]     = "function",
            ["function"] = new JsonObject
            {
                ["name"]        = t.Name,
                ["description"] = t.Description,
                ["parameters"]  = ParseSchema(t)
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
