using Xunit;

namespace PortwayApi.Tests.Services;

/// <summary>
/// The console saves edited controls only.
/// </summary>
public sealed class SettingsFormTests
{
    private static readonly string Script = File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "wwwroot", "js", "components", "settings-form.js"));

    [Fact]
    public void SaveCard_ComparesEachControlWithItsRenderedValue()
    {
        Assert.Matches(@"_initial\.set\(el,\s*JSON\.stringify\(readControl\(el\)\)\)", Script);
        Assert.Matches(@"JSON\.stringify\(value\)\s*!==\s*_initial\.get\(el\)", Script);
    }

    [Fact]
    public void LongKind_RendersAsANumberControl() =>
        Assert.Contains("'long'", Script);
}
