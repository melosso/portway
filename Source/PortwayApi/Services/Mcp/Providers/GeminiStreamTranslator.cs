namespace PortwayApi.Services.Mcp.Providers;

using System.Text.Json.Nodes;

/// <summary>Reads Gemini candidate parts, each carrying text or a complete function call</summary>
internal sealed class GeminiStreamTranslator : IChatStreamTranslator
{
    public bool IsComplete => false;

    public IEnumerable<ChatDelta> Translate(JsonNode chunk)
    {
        var parts = chunk["candidates"]?[0]?["content"]?["parts"]?.AsArray();
        if (parts is null) yield break;

        foreach (var part in parts)
        {
            var text = part?["text"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(text))
                yield return new ChatDelta { Type = ChatDeltaType.Text, Delta = text };

            var call = part?["functionCall"];
            if (call is not null)
            {
                yield return new ChatDelta
                {
                    Type      = ChatDeltaType.ToolCall,
                    ToolName  = call["name"]?.GetValue<string>(),
                    ToolInput = call["args"]?.ToJsonString()
                };
            }
        }
    }
}
