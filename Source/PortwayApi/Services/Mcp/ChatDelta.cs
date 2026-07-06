namespace PortwayApi.Services.Mcp;

using System.Text.Json.Serialization;

/// <summary>Discriminated union of all SSE event types streamed from the chat service. Serialised with snake_case (e.g. ToolCall → "tool_call")</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]

/// <summary>A single streamed chunk from the LLM; either a text delta or a completed tool call</summary>
public sealed record ChatDelta
{
    public ChatDeltaType Type  { get; init; } = ChatDeltaType.Text;
    public string? Delta       { get; init; } // text delta content
    public string? ToolName    { get; init; } // tool_call: which tool
    public string? ToolInput   { get; init; } // tool_call: raw JSON input
    public string? ToolResult  { get; init; } // tool_call: result after execution
}
