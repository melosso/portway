using System.Text.RegularExpressions;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

/// <summary>
/// The fetch wrapper in shared.js adds the path base, so a fetch call never adds it itself
/// </summary>
public sealed class FetchPathBaseTests
{
    [Fact]
    public void FetchCallsDoNotPrefixThePathBase()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        var doubled = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".html") || f.EndsWith(".js"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}vendor{Path.DirectorySeparatorChar}"))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"fetch\(\s*\(?\s*(base\s*\+|\(?window\.PortwayBase)")
                .Select(m => $"{Path.GetFileName(f)}: {m.Value}"))
            .ToList();

        Assert.Empty(doubled);
    }
}
