namespace PortwayApi.Services.Mcp;

using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Serilog;

/// <summary>
/// Anthropic Messages API (claude-* models) with streaming and tool use.
/// Uses the raw REST API — no SDK dependency.
/// </summary>
public sealed class AnthropicChatProvider(string apiKey, string model, IHttpClientFactory httpFactory) : IChatProvider
{
    private const string BaseUrl          = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async IAsyncEnumerable<ChatDelta> StreamAsync(
        IReadOnlyList<ChatMessage> history,
        IReadOnlyList<ToolDefinition> tools,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var messages = history.Select(m => new { role = m.Role, content = m.Content }).ToList();

        var toolDefs = tools.Select(t => new
        {
            name         = t.Name,
            description  = t.Description,
            input_schema = JsonNode.Parse(t.InputSchema) ?? new JsonObject()
        }).ToList();

        var body = JsonSerializer.Serialize(new
        {
            model,
            max_tokens = 4096,
            stream     = true,
            tools      = toolDefs,
            messages
        });

        using var http = httpFactory.CreateClient("mcp");
        http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        http.DefaultRequestHeaders.Add("anthropic-version", AnthropicVersion);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var req = new HttpRequestMessage(HttpMethod.Post, BaseUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        HttpResponseMessage? resp   = null;
        Exception?           sendEx = null;
        try
        {
            resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Anthropic API request failed");
            sendEx = ex;
        }

        if (sendEx is not null)
        {
            yield return new ChatDelta { Type = ChatDeltaType.Error, Delta = "Failed to reach Anthropic API." };
            yield return new ChatDelta { Type = ChatDeltaType.Done };
            yield break;
        }

        if (!resp!.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            Log.Error("Anthropic API error {Status}: {Body}", resp.StatusCode, err);
            yield return new ChatDelta { Type = ChatDeltaType.Error, Delta = $"Anthropic API error: {resp.StatusCode}" };
            yield return new ChatDelta { Type = ChatDeltaType.Done };
            yield break;
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new System.IO.StreamReader(stream);

        // Accumulate tool-use block state across streaming events
        string? currentToolName  = null;
        var     currentToolInput = new StringBuilder();
        bool    isDone           = false;

        while (!ct.IsCancellationRequested && !isDone)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            if (!line.StartsWith("data: ")) continue;

            var data = line["data: ".Length..];
            if (data == "[DONE]") break;

            JsonNode? node;
            try { node = JsonNode.Parse(data); }
            catch (Exception ex)
            {
                Log.Debug(ex, "Anthropic: skipping unparseable SSE chunk");
                continue;
            }

            var type = node?["type"]?.GetValue<string>();

            switch (type)
            {
                case "content_block_start":
                    var blockType = node?["content_block"]?["type"]?.GetValue<string>();
                    if (blockType == "tool_use")
                    {
                        currentToolName = node?["content_block"]?["name"]?.GetValue<string>();
                        currentToolInput.Clear();
                    }
                    break;

                case "content_block_delta":
                    var deltaType = node?["delta"]?["type"]?.GetValue<string>();
                    if (deltaType == "text_delta")
                    {
                        var text = node?["delta"]?["text"]?.GetValue<string>();
                        if (!string.IsNullOrEmpty(text))
                            yield return new ChatDelta { Type = ChatDeltaType.Text, Delta = text };
                    }
                    else if (deltaType == "input_json_delta")
                    {
                        var partial = node?["delta"]?["partial_json"]?.GetValue<string>();
                        if (partial is not null)
                            currentToolInput.Append(partial);
                    }
                    break;

                case "content_block_stop":
                    if (currentToolName is not null)
                    {
                        yield return new ChatDelta
                        {
                            Type      = ChatDeltaType.ToolCall,
                            ToolName  = currentToolName,
                            ToolInput = currentToolInput.ToString()
                        };
                        currentToolName = null;
                        currentToolInput.Clear();
                    }
                    break;

                case "message_stop":
                    isDone = true;
                    break;
            }
        }

        yield return new ChatDelta { Type = ChatDeltaType.Done };
    }
}
