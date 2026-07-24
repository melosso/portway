namespace PortwayApi.Services.Mcp.Providers;

using System.Text.Json.Nodes;

/// <summary>Translates one provider SSE chunk into chat deltas</summary>
public interface IChatStreamTranslator
{
    IEnumerable<ChatDelta> Translate(JsonNode chunk);

    bool IsComplete { get; }
}
