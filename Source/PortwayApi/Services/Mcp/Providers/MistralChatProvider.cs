namespace PortwayApi.Services.Mcp.Providers;

/// <summary>Mistral AI chat completions API with streaming and tool calling. Codestral models are served from a dedicated host</summary>
public sealed class MistralChatProvider(string apiKey, string model, IHttpClientFactory httpFactory)
    : OpenAiCompatibleChatProvider("Mistral", apiKey, model, httpFactory)
{
    protected override string BaseUrl => IsCodestral(Model)
        ? "https://codestral.mistral.ai/v1/chat/completions"
        : "https://api.mistral.ai/v1/chat/completions";

    private static bool IsCodestral(string model)
        => model.StartsWith("codestral", StringComparison.OrdinalIgnoreCase);
}
