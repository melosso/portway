namespace PortwayApi.Services.Mcp.Providers;

using System.Text;
using System.Text.Json.Nodes;

/// <summary>
/// Reads OpenAI choice deltas, accumulating tool call fragments per call index until the finish reason
/// </summary>
internal sealed class OpenAiCompatibleStreamTranslator : IChatStreamTranslator
{
    private readonly SortedDictionary<int, PendingCall> _pending = [];

    private sealed class PendingCall
    {
        public string? Id;
        public string? Name;
        public readonly StringBuilder Arguments = new();
    }

    public bool IsComplete => false;

    public IEnumerable<ChatDelta> Translate(JsonNode chunk)
    {
        var choice = chunk["choices"]?[0];
        var delta = choice?["delta"];
        if (delta is null) yield break;

        var text = delta["content"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(text))
            yield return new ChatDelta { Type = ChatDeltaType.Text, Delta = text };

        foreach (var call in delta["tool_calls"]?.AsArray() ?? [])
        {
            var index = call?["index"]?.GetValue<int>() ?? 0;
            if (!_pending.TryGetValue(index, out var pending))
                _pending[index] = pending = new PendingCall();

            pending.Id ??= call?["id"]?.GetValue<string>();
            pending.Name ??= call?["function"]?["name"]?.GetValue<string>();
            var args = call?["function"]?["arguments"]?.GetValue<string>();
            if (args is not null) pending.Arguments.Append(args);
        }

        if (choice?["finish_reason"]?.GetValue<string>() != "tool_calls") yield break;

        foreach (var pending in _pending.Values.Where(p => p.Name is not null))
        {
            yield return new ChatDelta
            {
                Type = ChatDeltaType.ToolCall,
                ToolName = pending.Name,
                ToolInput = pending.Arguments.ToString(),
                ToolCallId = pending.Id
            };
        }

        _pending.Clear();
    }
}
