namespace PortwayApi.Services.Mcp;

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Serilog;

/// <summary>
/// Google Gemini API (generateContent with streaming) with function declarations.
/// </summary>
public sealed class GeminiChatProvider(string apiKey, string model, IHttpClientFactory httpFactory) : IChatProvider
{
    private string BaseUrl =>
        $"https://generativelanguage.googleapis.com/v1beta/models/{model}:streamGenerateContent?alt=sse&key={apiKey}";

    public async IAsyncEnumerable<ChatDelta> StreamAsync(
        IReadOnlyList<ChatMessage> history,
        IReadOnlyList<ToolDefinition> tools,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Map chat history to Gemini "contents" format
        var contents = history.Select(m => new
        {
            role  = m.Role == "assistant" ? "model" : "user",
            parts = new[] { new { text = m.Content } }
        }).ToList();

        var functionDeclarations = tools.Select(t => new
        {
            name        = t.Name,
            description = t.Description,
            parameters  = JsonNode.Parse(t.InputSchema) ?? new JsonObject()
        }).ToList();

        var body = JsonSerializer.Serialize(new
        {
            contents,
            tools = new[] { new { functionDeclarations } }
        });

        using var http = httpFactory.CreateClient("mcp");
        using var req  = new HttpRequestMessage(HttpMethod.Post, BaseUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        HttpResponseMessage? resp   = null;
        Exception?           sendEx = null;
        try { resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct); }
        catch (Exception ex) { Log.Error(ex, "Gemini API request failed"); sendEx = ex; }

        if (sendEx is not null)
        {
            yield return new ChatDelta { Type = ChatDeltaType.Error, Delta = "Failed to reach Gemini API." };
            yield return new ChatDelta { Type = ChatDeltaType.Done };
            yield break;
        }

        if (!resp!.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            Log.Error("Gemini API error {Status}: {Body}", resp.StatusCode, err);
            yield return new ChatDelta { Type = ChatDeltaType.Error, Delta = $"Gemini API error: {resp.StatusCode}" };
            yield return new ChatDelta { Type = ChatDeltaType.Done };
            yield break;
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new System.IO.StreamReader(stream);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            if (!line.StartsWith("data: ")) continue;

            var data = line["data: ".Length..];

            JsonNode? node;
            try { node = JsonNode.Parse(data); }
            catch (Exception ex) { Log.Debug(ex, "Gemini: skipping unparseable SSE chunk"); continue; }

            var parts = node?["candidates"]?[0]?["content"]?["parts"]?.AsArray();
            if (parts is null) continue;

            foreach (var part in parts)
            {
                var text = part?["text"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(text))
                    yield return new ChatDelta { Type = ChatDeltaType.Text, Delta = text };

                var fnCall = part?["functionCall"];
                if (fnCall is not null)
                {
                    yield return new ChatDelta
                    {
                        Type      = ChatDeltaType.ToolCall,
                        ToolName  = fnCall["name"]?.GetValue<string>(),
                        ToolInput = fnCall["args"]?.ToJsonString()
                    };
                }
            }
        }

        yield return new ChatDelta { Type = ChatDeltaType.Done };
    }
}
