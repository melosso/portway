namespace PortwayApi.Services.Mcp;

using System.Text.Json.Serialization;

/// <summary>Discriminated union of all SSE event types streamed from the chat service. Serialised with snake_case (e.g. ToolCall → "tool_call")</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]

/// <summary>Implemented by each AI provider (Anthropic, OpenAI, Gemini, Mistral). Streams a conversation turn, yielding text deltas and tool-call events. The caller is responsible for executing tool calls and injecting results</summary>
public interface IChatProvider
{
    IAsyncEnumerable<ChatDelta> StreamAsync(
        IReadOnlyList<ChatMessage> history,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken ct = default);
}
