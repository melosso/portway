namespace PortwayApi.Services.Mcp;

/// <summary>A single tool offered to the LLM, built from a registered MCP endpoint</summary>
public sealed record ToolDefinition(
    string Name,
    string Description,            // LLM-facing: includes fields/environment metadata
    string InputSchema,            // JSON Schema as string
    string DisplayDescription = "", // Human-facing: plain summary shown in the UI
    bool   ReadOnly   = false,     // true when all registered methods are GET
    bool   Destructive = false     // true when any method is POST/PUT/PATCH/DELETE
);
