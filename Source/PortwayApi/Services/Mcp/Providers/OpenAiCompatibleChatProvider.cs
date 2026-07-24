namespace PortwayApi.Services.Mcp.Providers;

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

/// <summary>Base for providers speaking the OpenAI chat completions wire format</summary>
public abstract class OpenAiCompatibleChatProvider(
    string providerName,
    string apiKey,
    string model,
    IHttpClientFactory httpFactory) : SseChatProvider(providerName, httpFactory)
{
    protected string Model { get; } = model;

    protected abstract string BaseUrl { get; }

    protected override HttpRequestMessage CreateRequest(IReadOnlyList<ChatMessage> history, IReadOnlyList<ToolDefinition> tools)
    {
        var body = new JsonObject
        {
            ["model"]    = Model,
            ["stream"]   = true,
            ["tools"]    = ChatPayloadFactory.Functions(tools),
            ["messages"] = ChatPayloadFactory.Messages(history)
        };

        var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return request;
    }

    protected override IChatStreamTranslator CreateTranslator() => new OpenAiCompatibleStreamTranslator();
}
