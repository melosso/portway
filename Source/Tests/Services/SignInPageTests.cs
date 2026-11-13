using System.Text.RegularExpressions;
using Xunit;

namespace PortwayApi.Tests.Services;

/// <summary>
/// The sign-in page carries no inline script, handler or style: behaviour and styling come from files
/// </summary>
public class SignInPageTests
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
