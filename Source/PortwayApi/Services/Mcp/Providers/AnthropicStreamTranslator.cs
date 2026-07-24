namespace PortwayApi.Services.Mcp.Providers;

using System.Text;
using System.Text.Json.Nodes;

/// <summary>Reads Anthropic content block events, accumulating tool input until the block closes</summary>
internal sealed class AnthropicStreamTranslator : IChatStreamTranslator
{
    private readonly StringBuilder _currentToolInput = new();
    private string? _currentToolName;

    public bool IsComplete { get; private set; }

    public IEnumerable<ChatDelta> Translate(JsonNode chunk)
    {
        switch (chunk["type"]?.GetValue<string>())
        {
            case "content_block_start":
                if (chunk["content_block"]?["type"]?.GetValue<string>() == "tool_use")
                {
                    _currentToolName = chunk["content_block"]?["name"]?.GetValue<string>();
                    _currentToolInput.Clear();
                }
                break;

            case "content_block_delta":
                foreach (var delta in TranslateBlockDelta(chunk))
                    yield return delta;
                break;

            case "content_block_stop":
                if (_currentToolName is not null)
                {
                    yield return new ChatDelta
                    {
                        Type      = ChatDeltaType.ToolCall,
                        ToolName  = _currentToolName,
                        ToolInput = _currentToolInput.ToString()
                    };

                    _currentToolName = null;
                    _currentToolInput.Clear();
                }
                break;

            case "message_stop":
                IsComplete = true;
                break;
        }
    }

    private IEnumerable<ChatDelta> TranslateBlockDelta(JsonNode chunk)
    {
        var delta = chunk["delta"];

        switch (delta?["type"]?.GetValue<string>())
        {
            case "text_delta":
                var text = delta["text"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(text))
                    yield return new ChatDelta { Type = ChatDeltaType.Text, Delta = text };
                break;

            case "input_json_delta":
                var partial = delta["partial_json"]?.GetValue<string>();
                if (partial is not null)
                    _currentToolInput.Append(partial);
                break;
        }
    }
}
