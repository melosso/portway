namespace PortwayApi.Services.Mcp;

using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Serilog;

/// <summary>
/// Mistral AI chat completions API with streaming and tool/function calling.
/// Compatible with all Mistral models including Codestral (codestral-latest).
/// Codestral uses a separate endpoint: codestral.mistral.ai
/// </summary>
public sealed class MistralChatProvider(string apiKey, string model, IHttpClientFactory httpFactory) : IChatProvider
{
    // Codestral models use a dedicated endpoint
    private string BaseUrl => IsCodestral(model)
        ? "https://codestral.mistral.ai/v1/chat/completions"
        : "https://api.mistral.ai/v1/chat/completions";

    private static bool IsCodestral(string m) =>
        m.StartsWith("codestral", StringComparison.OrdinalIgnoreCase);

    public async IAsyncEnumerable<ChatDelta> StreamAsync(
        IReadOnlyList<ChatMessage> history,
        IReadOnlyList<ToolDefinition> tools,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var messages = history.Select(m => new { role = m.Role, content = m.Content }).ToList();

        var toolDefs = tools.Select(t => new
        {
            type     = "function",
            function = new
            {
                name        = t.Name,
                description = t.Description,
                parameters  = JsonNode.Parse(t.InputSchema) ?? new JsonObject()
            }
        }).ToList();

        var body = JsonSerializer.Serialize(new
        {
            model,
            stream = true,
            tools  = toolDefs,
            messages
        });

        using var http = httpFactory.CreateClient("mcp");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var req = new HttpRequestMessage(HttpMethod.Post, BaseUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, new MediaTypeHeaderValue("application/json"))
        };

        HttpResponseMessage? resp   = null;
        Exception?           sendEx = null;
        try { resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct); }
        catch (Exception ex) { Log.Error(ex, "Mistral API request failed"); sendEx = ex; }

        if (sendEx is not null)
        {
            yield return new ChatDelta { Type = ChatDeltaType.Error, Delta = "Failed to reach Mistral API." };
            yield return new ChatDelta { Type = ChatDeltaType.Done };
            yield break;
        }

        if (!resp!.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            Log.Error("Mistral API error {Status}: {Body}", resp.StatusCode, err);
            yield return new ChatDelta { Type = ChatDeltaType.Error, Delta = $"Mistral API error: {resp.StatusCode}" };
            yield return new ChatDelta { Type = ChatDeltaType.Done };
            yield break;
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new System.IO.StreamReader(stream);

        string? pendingToolName = null;
        var     pendingToolArgs = new StringBuilder();

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            if (!line.StartsWith("data: ")) continue;

            var data = line["data: ".Length..];
            if (data == "[DONE]") break;

            JsonNode? node;
            try { node = JsonNode.Parse(data); }
            catch (Exception ex) { Log.Debug(ex, "Mistral: skipping unparseable SSE chunk"); continue; }

            var delta = node?["choices"]?[0]?["delta"];
            if (delta is null) continue;

            var text = delta["content"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(text))
                yield return new ChatDelta { Type = ChatDeltaType.Text, Delta = text };

            var toolCalls = delta["tool_calls"]?.AsArray();
            if (toolCalls is not null)
            {
                foreach (var tc in toolCalls)
                {
                    var name = tc?["function"]?["name"]?.GetValue<string>();
                    if (name is not null) pendingToolName = name;

                    var args = tc?["function"]?["arguments"]?.GetValue<string>();
                    if (args is not null) pendingToolArgs.Append(args);
                }
            }

            var finishReason = node?["choices"]?[0]?["finish_reason"]?.GetValue<string>();
            if (finishReason == "tool_calls" && pendingToolName is not null)
            {
                yield return new ChatDelta
                {
                    Type      = ChatDeltaType.ToolCall,
                    ToolName  = pendingToolName,
                    ToolInput = pendingToolArgs.ToString()
                };
                pendingToolName = null;
                pendingToolArgs.Clear();
            }
        }

        yield return new ChatDelta { Type = ChatDeltaType.Done };
    }
}
