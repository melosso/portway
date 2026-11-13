using System.Text.RegularExpressions;
using Xunit;

namespace PortwayApi.Tests.Services;

/// <summary>
/// Console markup carries no inline event handler; the Content-Security-Policy runs inline scripts only with the request nonce
/// </summary>
public class ConsoleMarkupTests
{
    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine([AppContext.BaseDirectory, "wwwroot", .. parts]));

    [Fact]
    public void SignInPage_HasNoInlineCode()
    {
        var html = Read("_", "login.html");

        Assert.DoesNotMatch(@"\son[a-z]+=", html);
        Assert.DoesNotMatch(@"<script(?![^>]*\ssrc=)[^>]*>", html);
        Assert.DoesNotContain("style=", html);
    }

    [Fact]
    public void ConsoleFiles_HaveNoInlineEventHandlers()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        var files = Directory.EnumerateFiles(root, "*.html", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "js"), "*.js", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}vendor{Path.DirectorySeparatorChar}"));

        var offenders = files
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"\son[a-z]+=[""']"))
            .Select(f => Path.GetRelativePath(root, f))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void SignInPage_LabelsCarryTheFieldNames()
    {
        var html = Read("_", "login.html");

        Assert.DoesNotContain("placeholder=", html);
        Assert.Matches(new Regex(@"<label[^>]*for=""usernameInput"""), html);
        Assert.Matches(new Regex(@"<label[^>]*for=""passwordInput"""), html);
    }

    [Fact]
    public void ThemeToggle_UsesSharedSettingsKey()
    {
        var js = Read("js", "theme-toggle.js");

        Assert.Contains("'portway_settings'", js);
        Assert.Contains("prefers-color-scheme: dark", js);
    }
}
