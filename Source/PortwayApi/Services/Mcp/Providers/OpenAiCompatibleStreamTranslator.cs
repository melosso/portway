namespace PortwayApi.Services.Mcp.Providers;

using System.Text;
using System.Text.Json.Nodes;

/// <summary>Reads OpenAI choice deltas, accumulating tool call fragments until the finish reason</summary>
internal sealed class OpenAiCompatibleStreamTranslator : IChatStreamTranslator
{
    private readonly StringBuilder _pendingToolArgs = new();
    private string? _pendingToolName;

    public bool IsComplete => false;

    public IEnumerable<ChatDelta> Translate(JsonNode chunk)
    {
        var choice = chunk["choices"]?[0];
        var delta  = choice?["delta"];
        if (delta is null) yield break;

        var text = delta["content"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(text))
            yield return new ChatDelta { Type = ChatDeltaType.Text, Delta = text };

        foreach (var call in delta["tool_calls"]?.AsArray() ?? [])
        {
            var name = call?["function"]?["name"]?.GetValue<string>();
            if (name is not null) _pendingToolName = name;

            var args = call?["function"]?["arguments"]?.GetValue<string>();
            if (args is not null) _pendingToolArgs.Append(args);
        }

        var finishReason = choice?["finish_reason"]?.GetValue<string>();
        if (finishReason != "tool_calls" || _pendingToolName is null) yield break;

        yield return new ChatDelta
        {
            Type      = ChatDeltaType.ToolCall,
            ToolName  = _pendingToolName,
            ToolInput = _pendingToolArgs.ToString()
        };

        _pendingToolName = null;
        _pendingToolArgs.Clear();
    }
}
