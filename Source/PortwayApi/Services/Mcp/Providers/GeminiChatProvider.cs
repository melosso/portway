namespace PortwayApi.Services.Mcp.Providers;

using System.Text;
using System.Text.Json.Nodes;

/// <summary>Google Gemini API (streamGenerateContent) with function declarations</summary>
public sealed class GeminiChatProvider(string apiKey, string model, IHttpClientFactory httpFactory)
    : SseChatProvider("Gemini", httpFactory)
{
    private string BaseUrl =>
        $"https://generativelanguage.googleapis.com/v1beta/models/{model}:streamGenerateContent?alt=sse&key={apiKey}";

    protected override HttpRequestMessage CreateRequest(IReadOnlyList<ChatMessage> history, IReadOnlyList<ToolDefinition> tools)
    {
        var body = new JsonObject
        {
            ["contents"] = ChatPayloadFactory.GeminiContents(history),
            ["tools"]    = new JsonArray(new JsonObject
            {
                ["functionDeclarations"] = ChatPayloadFactory.Declarations(tools, "parameters")
            })
        };

        return new HttpRequestMessage(HttpMethod.Post, BaseUrl)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
    }

    protected override IChatStreamTranslator CreateTranslator() => new GeminiStreamTranslator();
}
