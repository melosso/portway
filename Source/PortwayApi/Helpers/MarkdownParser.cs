using System.Net;
using System.Text.RegularExpressions;

namespace PortwayApi.Helpers;

public static partial class MarkdownParser
{
    [GeneratedRegex(@"\*\*(.*?)\*\*")] private static partial Regex BoldAsterisks();
    [GeneratedRegex(@"__(.*?)__")] private static partial Regex BoldUnderscores();
    [GeneratedRegex(@"\*(.*?)\*")] private static partial Regex ItalicAsterisks();
    [GeneratedRegex(@"_(.*?)_")] private static partial Regex ItalicUnderscores();
    [GeneratedRegex(@"\[(.*?)\]\((.*?)\)")] private static partial Regex MarkdownLink();

    public static string ParseMarkdownToHtml(string md)
    {
        if (string.IsNullOrEmpty(md)) return "";

        var html = WebUtility.HtmlEncode(md);

        // Links become placeholders first: emphasis markers inside a URL stay in the href
        var links = new List<string>();
        html = MarkdownLink().Replace(html, m =>
        {
            links.Add(RenderLink(m.Groups[1].Value, m.Groups[2].Value));
            return $"\u0000{links.Count - 1}\u0000";
        });

        html = BoldAsterisks().Replace(html, "<strong>$1</strong>");
        html = BoldUnderscores().Replace(html, "<strong>$1</strong>");
        html = ItalicAsterisks().Replace(html, "<em>$1</em>");
        html = ItalicUnderscores().Replace(html, "<em>$1</em>");

        for (var i = 0; i < links.Count; i++)
            html = html.Replace($"\u0000{i}\u0000", links[i]);

        return html;
    }

    // Text and url arrive HTML-encoded; only http(s), protocol-relative and mailto URLs become links
    private static string RenderLink(string text, string url)
    {
        if (url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            return $"<a href=\"{url}\">{text}</a>";

        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("//", StringComparison.Ordinal))
            return $"<a href=\"{url}\" target=\"_blank\" rel=\"noopener noreferrer\">{text}</a>";

        return text;
    }
}
