using System.Net;
using System.Text.RegularExpressions;

namespace PortwayApi.Helpers;

public static partial class MarkdownParser
{
    [GeneratedRegex(@"\*\*(.*?)\*\*")] private static partial Regex BoldAsterisks();
    [GeneratedRegex(@"__(.*?)__")]     private static partial Regex BoldUnderscores();
    [GeneratedRegex(@"\*(.*?)\*")]     private static partial Regex ItalicAsterisks();
    [GeneratedRegex(@"_(.*?)_")]       private static partial Regex ItalicUnderscores();
    [GeneratedRegex(@"\[(.*?)\]\((.*?)\)")] private static partial Regex MarkdownLink();

    public static string ParseMarkdownToHtml(string md)
    {
        if (string.IsNullOrEmpty(md)) return "";

        var html = WebUtility.HtmlEncode(md);

        html = BoldAsterisks().Replace(html,     "<strong>$1</strong>");
        html = BoldUnderscores().Replace(html,   "<strong>$1</strong>");
        html = ItalicAsterisks().Replace(html,   "<em>$1</em>");
        html = ItalicUnderscores().Replace(html, "<em>$1</em>");

        // Reject javascript:, data:, vbscript: and any other non-http(s) scheme.
        // HtmlEncode does not strip these — only allow relative (//) or http(s) URLs.
        html = MarkdownLink().Replace(html, m =>
        {
            var text = m.Groups[1].Value;
            var url  = m.Groups[2].Value;

            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("http://",  StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("//",       StringComparison.Ordinal))
                return WebUtility.HtmlEncode(text);

            return $"<a href=\"{url}\" target=\"_blank\" rel=\"noopener noreferrer\">{text}</a>";
        });

        return html;
    }
}
