namespace PortwayApi.Services.Mcp.Providers;

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

/// <summary>Providers speaking the OpenAI chat completions wire format; they differ only by name and base URL</summary>
public sealed class OpenAiCompatibleChatProvider(
    string providerName,
    string apiKey,
    string model,
    string baseUrl,
    IHttpClientFactory httpFactory) : SseChatProvider(providerName, httpFactory)
{
    public const string OpenAiUrl = "https://api.openai.com/v1/chat/completions";
    public const string MistralUrl = "https://api.mistral.ai/v1/chat/completions";
    public const string CodestralUrl = "https://codestral.mistral.ai/v1/chat/completions";

    /// <summary>Mistral serves codestral models from a dedicated host</summary>
    public static string MistralUrlFor(string model)
        => model.StartsWith("codestral", StringComparison.OrdinalIgnoreCase) ? CodestralUrl : MistralUrl;

    protected override HttpRequestMessage CreateRequest(IReadOnlyList<ChatMessage> history, IReadOnlyList<ToolDefinition> tools)
    {
        var body = new JsonObject
        {
            ["model"]    = model,
            ["stream"]   = true,
            ["tools"]    = ChatPayloadFactory.Functions(tools),
            ["messages"] = ChatPayloadFactory.Messages(history)
        };

        var request = new HttpRequestMessage(HttpMethod.Post, baseUrl)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return request;
    }

    protected override IChatStreamTranslator CreateTranslator() => new OpenAiCompatibleStreamTranslator();
}
