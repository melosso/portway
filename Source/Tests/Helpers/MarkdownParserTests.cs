using PortwayApi.Helpers;
using Xunit;

namespace PortwayApi.Tests.Helpers;

public class MarkdownParserTests
{
    [Theory]
    [InlineData("[click](https://example.com)", "https://example.com")]
    [InlineData("[click](http://example.com)", "http://example.com")]
    [InlineData("[click](//example.com/path)", "//example.com/path")]
    public void ParseMarkdownToHtml_SafeUrl_RendersAnchor(string md, string expectedHref)
    {
        var html = MarkdownParser.ParseMarkdownToHtml(md);
        Assert.Contains($"href=\"{expectedHref}\"", html);
    }

    [Theory]
    [InlineData("[xss](javascript:alert(1))")]
    [InlineData("[xss](javascript:void(0))")]
    [InlineData("[xss](data:text/html,<script>alert(1)</script>)")]
    [InlineData("[xss](vbscript:msgbox(1))")]
    public void ParseMarkdownToHtml_UnsafeScheme_RendersTextOnly(string md)
    {
        var html = MarkdownParser.ParseMarkdownToHtml(md);
        Assert.DoesNotContain("<a ", html);
        Assert.DoesNotContain("javascript:", html);
        Assert.DoesNotContain("data:", html);
        Assert.DoesNotContain("vbscript:", html);
    }

    [Fact]
    public void ParseMarkdownToHtml_Bold_Renders()
        => Assert.Contains("<strong>bold</strong>", MarkdownParser.ParseMarkdownToHtml("**bold**"));

    [Fact]
    public void ParseMarkdownToHtml_Italic_Renders()
        => Assert.Contains("<em>italic</em>", MarkdownParser.ParseMarkdownToHtml("*italic*"));

    [Fact]
    public void ParseMarkdownToHtml_EmptyString_ReturnsEmpty()
        => Assert.Equal("", MarkdownParser.ParseMarkdownToHtml(""));
}
