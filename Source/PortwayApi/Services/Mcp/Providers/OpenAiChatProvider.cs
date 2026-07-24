namespace PortwayApi.Services.Mcp.Providers;

/// <summary>OpenAI Chat Completions API with streaming and function calling</summary>
public sealed class OpenAiChatProvider(string apiKey, string model, IHttpClientFactory httpFactory)
    : OpenAiCompatibleChatProvider("OpenAI", apiKey, model, httpFactory)
{
    protected override string BaseUrl => "https://api.openai.com/v1/chat/completions";
}
