namespace PortwayApi.Services.Mcp;

public sealed record ChatOptions
{
    public bool    Enabled      { get; init; } = false;
    public string  Provider     { get; init; } = "Anthropic";
    public string  Model        { get; init; } = "claude-sonnet-4-6";
    /// <summary>Name of the environment variable that holds the API key. Takes priority over ApiKey.</summary>
    public string  ApiKeyEnvVar { get; init; } = "PORTWAY_CHAT_API_KEY";
    /// <summary>
    /// API key stored directly in config. May be plaintext (auto-encrypted on startup) or
    /// PWENC-encrypted. Use ApiKeyEnvVar instead when possible.
    /// </summary>
    public string? ApiKey           { get; init; }
    /// <summary>
    /// Portway Bearer token used when the chat service calls endpoints internally to execute tool calls.
    /// Must have access to all environments and endpoints exposed as MCP tools.
    /// </summary>
    public string? InternalApiToken { get; init; }
}
