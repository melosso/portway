using System.Text.RegularExpressions;
using Xunit;

namespace PortwayApi.Tests.Services;

/// <summary>
/// Restore dialogs overwrite live configuration, so their confirm button stays disabled for ten seconds
/// </summary>
public class ConsoleDialogGuardTests
{
    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine([AppContext.BaseDirectory, "wwwroot", .. parts]));

    [Fact]
    public void BackupRestoreDialog_WaitsTenSecondsBeforeConfirm()
    {
        var settings = Read("ui", "views", "settings.html");
        var dialog = Regex.Match(settings, @"function restoreBackup\(name\)\s*\{.*?onConfirm", RegexOptions.Singleline);

        Assert.True(dialog.Success);
        Assert.Matches(@"confirmDelaySeconds:\s*10\b", dialog.Value);
    }

    [Fact]
    public void ConfigurationRestoreDialog_WaitsTenSecondsBeforeConfirm()
    {
        var settings = Read("ui", "views", "settings.html");
        var dialog = Regex.Match(settings, @"function restoreChange\(id\)\s*\{.*?onConfirm", RegexOptions.Singleline);

        Assert.True(dialog.Success);
        Assert.Matches(@"confirmDelaySeconds:\s*10\b", dialog.Value);
    }

    [Fact]
    public void AlertDialog_DisablesActionDuringDelayAndIgnoresDisabledClicks()
    {
        var js = Read("js", "components", "alert-dialog.js");

        Assert.Contains("actionBtn.disabled = true;", js);
        Assert.Contains("if (e.currentTarget.disabled) return;", js);
        Assert.Contains("clearTimeout(this._delayTimer);", js);
    }
}
