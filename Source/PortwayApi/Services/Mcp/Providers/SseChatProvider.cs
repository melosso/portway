namespace PortwayApi.Services.Mcp.Providers;

using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Serilog;

/// <summary>Shared SSE transport: sends the turn, reports failures as deltas, feeds chunks to a translator</summary>
public abstract class SseChatProvider(string providerName, IHttpClientFactory httpFactory) : IChatProvider
{
    private const string DataPrefix = "data: ";
    private const string DoneMarker = "[DONE]";

    protected string ProviderName { get; } = providerName;

    protected abstract HttpRequestMessage CreateRequest(IReadOnlyList<ChatMessage> history, IReadOnlyList<ToolDefinition> tools);

    protected abstract IChatStreamTranslator CreateTranslator();

    public async IAsyncEnumerable<ChatDelta> StreamAsync(
        IReadOnlyList<ChatMessage> history,
        IReadOnlyList<ToolDefinition> tools,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var http = httpFactory.CreateClient("mcp");
        using var request = CreateRequest(history, tools);

        HttpResponseMessage? response = null;
        Exception? sendFailure = null;
        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Log.Error(ex, "{Provider} API request failed", ProviderName);
            sendFailure = ex;
        }

        if (sendFailure is not null)
        {
            yield return Error($"Failed to reach {ProviderName} API.");
            yield return new ChatDelta { Type = ChatDeltaType.Done };
            yield break;
        }

        using (response)
        {
            if (!response!.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                Log.Error("{Provider} API error {Status}: {Body}", ProviderName, response.StatusCode, body);
                yield return Error($"{ProviderName} API error: {response.StatusCode}");
                yield return new ChatDelta { Type = ChatDeltaType.Done };
                yield break;
            }

            var translator = CreateTranslator();

            await foreach (var chunk in ReadChunksAsync(response, ct))
            {
                foreach (var delta in translator.Translate(chunk))
                    yield return delta;

                if (translator.IsComplete) break;
            }
        }

        yield return new ChatDelta { Type = ChatDeltaType.Done };
    }

    private async IAsyncEnumerable<JsonNode> ReadChunksAsync(
        HttpResponseMessage response,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            if (!line.StartsWith(DataPrefix, StringComparison.Ordinal)) continue;

            var data = line[DataPrefix.Length..];
            if (data == DoneMarker) break;

            JsonNode? node;
            try
            {
                node = JsonNode.Parse(data);
            }
            catch (JsonException ex)
            {
                Log.Debug(ex, "{Provider}: skipping unparseable SSE chunk", ProviderName);
                continue;
            }

            if (node is not null)
                yield return node;
        }
    }

    private static ChatDelta Error(string message) => new() { Type = ChatDeltaType.Error, Delta = message };
}
