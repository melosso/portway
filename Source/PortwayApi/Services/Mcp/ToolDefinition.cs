namespace PortwayApi.Services.Mcp;

using System.Text.Json.Serialization;

/// <summary>Discriminated union of all SSE event types streamed from the chat service. Serialised with snake_case (e.g. ToolCall → "tool_call")</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]

public sealed record ToolDefinition(
    string Name,
    string Description,            // LLM-facing: includes fields/environment metadata
    string InputSchema,            // JSON Schema as string
    string DisplayDescription = "", // Human-facing: plain summary shown in the UI
    bool   ReadOnly   = false,     // true when all registered methods are GET
    bool   Destructive = false     // true when any method is POST/PUT/PATCH/DELETE
);
