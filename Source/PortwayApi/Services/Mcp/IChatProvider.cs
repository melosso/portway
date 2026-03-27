namespace PortwayApi.Services.Mcp;

using System.Text.Json.Serialization;

/// <summary>
/// Discriminated union of all SSE event types streamed from the chat service.
/// Serialised with snake_case (e.g. ToolCall → "tool_call").
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ChatDeltaType
{
    Text,
    ToolCall,
    Done,
    Thinking,
    Error
}

/// <summary>
/// A single streamed chunk from the LLM — either a text delta or a completed tool call.
/// </summary>
public sealed record ChatDelta
{
    public ChatDeltaType Type  { get; init; } = ChatDeltaType.Text;
    public string? Delta       { get; init; } // text delta content
    public string? ToolName    { get; init; } // tool_call: which tool
    public string? ToolInput   { get; init; } // tool_call: raw JSON input
    public string? ToolResult  { get; init; } // tool_call: result after execution
}

public sealed record ChatMessage(string Role, string Content);

public sealed record ToolDefinition(
    string Name,
    string Description,            // LLM-facing: includes fields/environment metadata
    string InputSchema,            // JSON Schema as string
    string DisplayDescription = "", // Human-facing: plain summary shown in the UI
    bool   ReadOnly   = false,     // true when all registered methods are GET
    bool   Destructive = false     // true when any method is POST/PUT/PATCH/DELETE
);

/// <summary>
/// Implemented by each AI provider (Anthropic, OpenAI, Gemini, Mistral).
/// Streams a conversation turn, yielding text deltas and tool-call events.
/// The caller is responsible for executing tool calls and injecting results.
/// </summary>
public interface IChatProvider
{
    IAsyncEnumerable<ChatDelta> StreamAsync(
        IReadOnlyList<ChatMessage> history,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken ct = default);
}
