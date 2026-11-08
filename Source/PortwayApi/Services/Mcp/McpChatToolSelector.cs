namespace PortwayApi.Services.Mcp;

using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

/// <summary>
/// Limits the tools sent per round; above the limit the model finds the others through a lookup tool
/// </summary>
internal static partial class McpChatToolSelector
{
    internal const int Limit = 25;
    internal const string FindToolsName = "portway_find_tools";
    private const int MaxMatches = 15;

    internal static readonly ToolDefinition FindTools = new(
        FindToolsName,
        "Search all Portway tools by keywords. Returns matching tool names and descriptions. The named tools are available in the next round.",
        """{"type":"object","properties":{"query":{"type":"string","description":"Keywords, e.g. 'stock warehouse'"}},"required":["query"]}""",
        "Find tools",
        ReadOnly: true);

    [GeneratedRegex(@"[\p{L}\p{N}]{3,}")]
    private static partial Regex WordPattern();

    /// <summary>
    /// All tools up to the limit; otherwise the requested tools, the best matches for the question, and the lookup tool
    /// </summary>
    internal static IReadOnlyList<ToolDefinition> Select(IReadOnlyList<ToolDefinition> all, string question, IReadOnlySet<string> requested)
    {
        if (all.Count <= Limit) return all;

        var words = Words(question);
        var chosen = all.Where(t => requested.Contains(t.Name)).Take(Limit - 1).ToList();
        chosen.AddRange(all
            .Where(t => !requested.Contains(t.Name))
            .Select(t => (Tool: t, Score: Score(t, words)))
            .OrderByDescending(x => x.Score)
            .Take(Limit - 1 - chosen.Count)
            .Select(x => x.Tool));
        chosen.Add(FindTools);
        return chosen;
    }

    /// <summary>
    /// Tool names and descriptions matching the query, best first
    /// </summary>
    internal static IReadOnlyList<ToolDefinition> Find(IReadOnlyList<ToolDefinition> all, string inputJson)
    {
        string query;
        try { query = JsonNode.Parse(inputJson)?["query"]?.GetValue<string>() ?? string.Empty; }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException) { query = string.Empty; }

        var words = Words(query);
        return [.. all
            .Select(t => (Tool: t, Score: Score(t, words)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(MaxMatches)
            .Select(x => x.Tool)];
    }

    internal static string Describe(IReadOnlyList<ToolDefinition> found) => found.Count == 0
        ? "No tools match. Use other keywords, e.g. a synonym or the English term."
        : string.Join("\n", found.Select(t => $"{t.Name}: {(t.Description.Length > 160 ? t.Description[..160] + "..." : t.Description)}"));

    private static HashSet<string> Words(string text) =>
        WordPattern().Matches(text.ToLowerInvariant()).Select(m => m.Value).ToHashSet();

    private static int Score(ToolDefinition tool, HashSet<string> words)
    {
        var text = Words($"{tool.Name.Replace('_', ' ')} {tool.Description}");
        return words.Count(w => text.Any(t => t.StartsWith(w, StringComparison.Ordinal) || w.StartsWith(t, StringComparison.Ordinal)));
    }
}
