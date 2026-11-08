using PortwayApi.Services.Mcp;
using Xunit;

namespace PortwayApi.Tests.Services;

/// <summary>
/// Early tool results shrink to their facts and keep the first rows
/// </summary>
public class McpChatHistorySummaryTests
{
    private static readonly string Collection =
        "{\"success\":true,\"count\":50,\"totalCount\":512,\"value\":[" +
        string.Join(",", Enumerable.Range(1, 50).Select(i => $"{{\"ItemCode\":\"A{i}\",\"Stock\":{i}}}")) + "]}";

    [Fact]
    public void Collection_KeepsCountsFieldsAndFirstRows()
    {
        var summary = McpChatService.SummariseToolResult(Collection, 300);

        Assert.StartsWith("[earlier result] rows: 50; totalCount: 512; fields: ItemCode, Stock", summary);
        Assert.Contains("\"ItemCode\":\"A1\"", summary);
        Assert.True(summary.Length <= 306);
    }

    [Fact]
    public void Error_KeepsTheMessage()
    {
        var summary = McpChatService.SummariseToolResult("{\"success\":false,\"error\":\"Unknown field Stok\",\"traceId\":\"t\"}", 300);

        Assert.Equal("[earlier result] error: \"Unknown field Stok\"\n", summary);
    }

    [Fact]
    public void Text_KeepsTheStart()
    {
        var summary = McpChatService.SummariseToolResult(new string('x', 1000), 100);

        Assert.StartsWith("[earlier result]\nxxx", summary);
        Assert.EndsWith(" [...]", summary);
    }

    [Fact]
    public void History_SummarisesOnlyEarlierRounds_Once()
    {
        var call = new ChatToolCall("1", "items", "{}");
        var history = new List<ChatMessage> { ChatMessage.ToolResult(call, Collection), ChatMessage.ToolResult(call, Collection) };

        McpChatService.TrimEarlyToolResultsInHistory(history, before: 1, maxChars: 300);
        var once = history[0].Content;
        McpChatService.TrimEarlyToolResultsInHistory(history, before: 1, maxChars: 300);

        Assert.StartsWith("[earlier result]", once);
        Assert.Equal(once, history[0].Content);
        Assert.Equal(Collection, history[1].Content);
    }
}
