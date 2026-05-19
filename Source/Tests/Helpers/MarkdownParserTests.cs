using System;
using PortwayApi.Helpers;
using Xunit;

namespace PortwayApi.Tests.Helpers;

public class MarkdownParserTests
{
    #region Links

    [Theory]
    [InlineData("[click](https://example.com)", "https://example.com")]
    [InlineData("[click](http://example.com)", "http://example.com")]
    [InlineData("[click](//example.com/path)", "//example.com/path")]
    [InlineData("[click](/relative/path)", "/relative/path")]
    [InlineData("[click](#anchor-link)", "#anchor-link")]
    [InlineData("[email](mailto:test@example.com)", "mailto:test@example.com")]
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
    [InlineData("[xss]( javascript:alert(1))")] // Leading whitespace bypass attempt
    [InlineData("[xss](JaVaScRiPt:alert(1))")]  // Case-insensitivity bypass attempt
    [InlineData("[xss](javascript&#58;alert(1))")] // HTML entity bypass attempt
    public void ParseMarkdownToHtml_UnsafeScheme_RendersTextOnlyOrStrips(string md)
    {
        var html = MarkdownParser.ParseMarkdownToHtml(md);
        Assert.DoesNotContain("<a ", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vbscript:", html, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Images

    [Theory]
    [InlineData("![logo](https://example.com/logo.png)", "https://example.com/logo.png", "logo")]
    [InlineData("![alt text](/local/image.jpg)", "/local/image.jpg", "alt text")]
    public void ParseMarkdownToHtml_SafeImage_RendersImgTag(string md, string expectedSrc, string expectedAlt)
    {
        var html = MarkdownParser.ParseMarkdownToHtml(md);
        Assert.Contains("<img ", html);
        Assert.Contains($"src=\"{expectedSrc}\"", html);
        Assert.Contains($"alt=\"{expectedAlt}\"", html);
    }

    [Theory]
    [InlineData("![xss](javascript:alert(1))")]
    [InlineData("![xss](data:image/svg+xml,<svg onload=alert(1)>)")]
    public void ParseMarkdownToHtml_UnsafeImage_RendersTextOnlyOrStrips(string md)
    {
        var html = MarkdownParser.ParseMarkdownToHtml(md);
        Assert.DoesNotContain("<img ", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onload=", html, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Formatting & Inline Elements

    [Fact]
    public void ParseMarkdownToHtml_Bold_Renders()
        => Assert.Contains("<strong>bold</strong>", MarkdownParser.ParseMarkdownToHtml("**bold**"));

    [Fact]
    public void ParseMarkdownToHtml_Italic_Renders()
        => Assert.Contains("<em>italic</em>", MarkdownParser.ParseMarkdownToHtml("*italic*"));

    [Fact]
    public void ParseMarkdownToHtml_BoldAndItalic_RendersBoth()
    {
        var html = MarkdownParser.ParseMarkdownToHtml("***bold and italic***");
        Assert.Contains("<strong><em>bold and italic</em></strong>", html);
    }

    [Fact]
    public void ParseMarkdownToHtml_InlineCode_Renders()
    {
        var html = MarkdownParser.ParseMarkdownToHtml("Use `var x = 1;`");
        Assert.Contains("<code>var x = 1;</code>", html);
    }

    [Fact]
    public void ParseMarkdownToHtml_Strikethrough_Renders()
    {
        var html = MarkdownParser.ParseMarkdownToHtml("~~deleted~~");
        // Depending on your parser, this might be <del> or <s>
        Assert.True(html.Contains("<del>deleted</del>") || html.Contains("<s>deleted</s>"));
    }

    #endregion

    #region Structural Elements

    [Theory]
    [InlineData("# Heading 1", "<h1>Heading 1</h1>")]
    [InlineData("## Heading 2", "<h2>Heading 2</h2>")]
    [InlineData("### Heading 3", "<h3>Heading 3</h3>")]
    public void ParseMarkdownToHtml_Headings_Renders(string md, string expectedHtml)
    {
        var html = MarkdownParser.ParseMarkdownToHtml(md);
        Assert.Contains(expectedHtml, html);
    }

    [Fact]
    public void ParseMarkdownToHtml_Blockquote_Renders()
    {
        var html = MarkdownParser.ParseMarkdownToHtml("> This is a quote");
        Assert.Contains("<blockquote>", html);
        Assert.Contains("This is a quote", html);
    }

    [Fact]
    public void ParseMarkdownToHtml_UnorderedList_Renders()
    {
        var html = MarkdownParser.ParseMarkdownToHtml("- Item 1\n- Item 2");
        Assert.Contains("<ul>", html);
        Assert.Contains("<li>Item 1</li>", html);
        Assert.Contains("<li>Item 2</li>", html);
        Assert.Contains("</ul>", html);
    }

    [Fact]
    public void ParseMarkdownToHtml_CodeBlock_Renders()
    {
        var md = "```csharp\npublic void Test() {}\n
```";
        var html = MarkdownParser.ParseMarkdownToHtml(md);
        Assert.Contains("<pre>", html);
        Assert.Contains("<code>", html);
        Assert.Contains("public void Test() {}", html);
    }

    #endregion

    #region Security & Edge Cases

    [Fact]
    public void ParseMarkdownToHtml_EmptyString_ReturnsEmpty()
        => Assert.Equal(string.Empty, MarkdownParser.ParseMarkdownToHtml(string.Empty));

    [Fact]
    public void ParseMarkdownToHtml_NullInput_ReturnsEmptyOrThrows()
    {
        // Depending on your implementation, it may return empty string or throw ArgumentNullException.
        // Update this assertion based on expected behavior.
        var html = MarkdownParser.ParseMarkdownToHtml(null);
        Assert.Equal(string.Empty, html);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<iframe src=\"javascript:alert(1)\"></iframe>")]
    [InlineData("<b onmouseover=\"alert(1)\">bold</b>")]
    public void ParseMarkdownToHtml_RawHtml_IsEscapedOrStripped(string md)
    {
        var html = MarkdownParser.ParseMarkdownToHtml(md);
        // The parser should ideally HTML-encode raw tags or strip them entirely
        Assert.DoesNotContain("<script>", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<iframe", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onmouseover", html, StringComparison.OrdinalIgnoreCase);
    }

    #endregion
}