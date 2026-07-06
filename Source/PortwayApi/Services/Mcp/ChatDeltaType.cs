namespace PortwayApi.Services.Mcp;

using System.Text.Json.Serialization;

/// <summary>Discriminated union of all SSE event types streamed from the chat service. Serialised with snake_case (e.g. ToolCall → "tool_call")</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]

public enum ChatDeltaType
{
    Text,
    ToolCall,
    Done,
    Thinking,
    Error
}
