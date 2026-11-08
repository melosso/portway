using PortwayApi.Services.Mcp;
using Xunit;

namespace PortwayApi.Tests.Services;

/// <summary>
/// Above the limit a round sends the best matches plus the lookup tool
/// </summary>
public class McpChatToolSelectorTests
{
    private static ToolDefinition Tool(string name, string description = "Generic records") => new(name, description, "{}");

    private static readonly ToolDefinition[] Many =
    [
        Tool("inventory_stock", "Stock per warehouse"),
        Tool("finance_invoices", "Outstanding invoices"),
        .. Enumerable.Range(0, 40).Select(i => Tool($"misc_{i}"))
    ];

    private static readonly HashSet<string> None = [];

    [Fact]
    public void UpToTheLimit_SendsAllTools()
    {
        var few = Many.Take(McpChatToolSelector.Limit).ToArray();

        Assert.Same(few, McpChatToolSelector.Select(few, "stock", None));
    }

    [Fact]
    public void AboveTheLimit_SendsBestMatchesAndTheLookup()
    {
        var selected = McpChatToolSelector.Select(Many, "What is the stock per warehouse?", None);

        Assert.Equal(McpChatToolSelector.Limit, selected.Count);
        Assert.Equal("inventory_stock", selected[0].Name);
        Assert.Equal(McpChatToolSelector.FindToolsName, selected[^1].Name);
    }

    [Fact]
    public void RequestedTools_AreAlwaysSent()
    {
        var selected = McpChatToolSelector.Select(Many, "stock", new HashSet<string> { "misc_39" });

        Assert.Contains(selected, t => t.Name == "misc_39");
    }

    [Fact]
    public void Find_ReturnsMatchesByKeyword()
    {
        var found = McpChatToolSelector.Find(Many, """{"query":"invoices"}""");

        Assert.Equal("finance_invoices", Assert.Single(found).Name);
        Assert.StartsWith("finance_invoices: Outstanding invoices", McpChatToolSelector.Describe(found));
    }

    [Theory]
    [InlineData("""{"query":"nothing-like-this"}""")]
    [InlineData("not json")]
    [InlineData("""{"query":5}""")]
    public void Find_WithoutMatches_SaysSo(string input)
    {
        Assert.Empty(McpChatToolSelector.Find(Many, input));
        Assert.StartsWith("No tools match.", McpChatToolSelector.Describe([]));
    }
}
