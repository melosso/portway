namespace PortwayApi.Classes;

/// <summary>MCP exposure settings for an endpoint</summary>
public class McpSettings
{
    /// <summary>Whether this endpoint is exposed in MCP</summary>
    public bool Exposed { get; set; } = false;
    /// <summary>Usage instruction appended to the tool description when registered with MCP</summary>
    public string? Instruction { get; set; }
}
