using Xunit;

namespace PortwayApi.Tests.Services;

/// <summary>
/// chat.html's markdown renderer must escape quotes and allowlist link URL schemes
/// </summary>
public class ChatHtmlXssTests
{
    private static string ReadChatHtml()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "wwwroot", "ui", "mcp", "chat.html");
        Assert.True(File.Exists(path), $"chat.html not found at {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void MarkdownEscaper_EscapesQuotesAsWellAsAngleBrackets()
    {
        var html = ReadChatHtml();

        Assert.Contains("replace(/\"/g, '&quot;')", html);
        Assert.Contains("replace(/'/g, '&#39;')", html);
    }

    [Fact]
    public void MarkdownLinkBuilder_AllowlistsUrlSchemeBeforeBuildingAnchor()
    {
        var html = ReadChatHtml();

        // The old vulnerable pattern interpolated the captured URL straight into href
        Assert.DoesNotContain("'<a href=\"$2\"", html);
        Assert.Contains("^(https?:|mailto:)", html);
    }
}
