namespace PortwayApi.Services.Mcp;

/// <summary>Implemented by each AI provider (Anthropic, OpenAI, Gemini, Mistral). Streams a conversation turn, yielding text deltas and tool-call events. The caller is responsible for executing tool calls and injecting results</summary>
public interface IChatProvider
{
    IAsyncEnumerable<ChatDelta> StreamAsync(
        IReadOnlyList<ChatMessage> history,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken ct = default);
}
