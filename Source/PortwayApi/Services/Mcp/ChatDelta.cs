namespace PortwayApi.Services.Mcp;

/// <summary>
/// A single streamed chunk from the LLM; either a text delta or a completed tool call
/// </summary>
public sealed record ChatDelta
{
    public ChatDeltaType Type { get; init; } = ChatDeltaType.Text;
    public string? Delta { get; init; } // text delta content
    public string? ToolName { get; init; } // tool_call: which tool
    public string? ToolInput { get; init; } // tool_call: raw JSON input
    public string? ToolResult { get; init; } // tool_call: result after execution
    [System.Text.Json.Serialization.JsonIgnore] public string? ToolCallId { get; init; }
    // gemini thought signature, returned with the call in the next request
    [System.Text.Json.Serialization.JsonIgnore] public string? ToolSignature { get; init; }
}
