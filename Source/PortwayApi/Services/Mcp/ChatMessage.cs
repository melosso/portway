namespace PortwayApi.Services.Mcp;

/// <summary>
/// One turn of chat history sent to the provider
/// </summary>
public sealed record ChatMessage(string Role, string Content)
{
    public const string System = "system";
    public const string User = "user";
    public const string Assistant = "assistant";
    public const string Tool = "tool";

    /// <summary>
    /// Tool calls the assistant requested in this turn
    /// </summary>
    public IReadOnlyList<ChatToolCall> ToolCalls { get; init; } = [];

    /// <summary>
    /// The call this tool result answers
    /// </summary>
    public ChatToolCall? ResultOf { get; init; }

    public static ChatMessage ToolResult(ChatToolCall call, string content) => new(Tool, content) { ResultOf = call };
}

/// <summary>
/// A tool call as the provider issued it; the id pairs it with its result
/// </summary>
public sealed record ChatToolCall(string Id, string Name, string Input, string? Signature = null);
