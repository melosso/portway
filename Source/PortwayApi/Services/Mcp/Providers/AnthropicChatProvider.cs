namespace PortwayApi.Services.Mcp.Providers;

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

/// <summary>Anthropic Messages API (claude-* models) with streaming and tool use. Uses the raw REST API, no SDK dependency</summary>
public sealed class AnthropicChatProvider(string apiKey, string model, IHttpClientFactory httpFactory)
    : SseChatProvider("Anthropic", httpFactory)
{
    private const string BaseUrl          = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";
    private const int    MaxTokens        = 4096;

    protected override HttpRequestMessage CreateRequest(IReadOnlyList<ChatMessage> history, IReadOnlyList<ToolDefinition> tools)
    {
        var body = new JsonObject
        {
            ["model"]      = model,
            ["max_tokens"] = MaxTokens,
            ["stream"]     = true,
            ["tools"]      = ChatPayloadFactory.Declarations(tools, "input_schema"),
            ["messages"]   = ChatPayloadFactory.Messages(history)
        };

        var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };

        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", AnthropicVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        return request;
    }

    protected override IChatStreamTranslator CreateTranslator() => new AnthropicStreamTranslator();
}
