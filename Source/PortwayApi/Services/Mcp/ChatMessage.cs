namespace PortwayApi.Services.Mcp;

/// <summary>One turn of chat history sent to the provider</summary>
public sealed record ChatMessage(string Role, string Content);
