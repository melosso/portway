using PortwayApi.Helpers;
using Xunit;

namespace PortwayApi.Tests.Helpers;

/// <summary>
/// A hardcoded fallback key must never protect secrets outside Development
/// </summary>
public class SettingsEncryptionHelperTests
{
    private const string FallbackKey =
        "$XTSI5gTEf1hawq3G2uOdWTsFUrgZ6mkCBGrdr0fsRTegXwis68HxGEoCsIBpgbPl5swwY9BQ0qiXG6CaeEPJzp3SPyGebl0ZyHL3jLACKIuSw7G1ufAZ5XATtetKatH0sr#";

    [Fact]
    public void ThrowsOutsideDevelopmentWithoutRealKey() =>
        Assert.Throws<InvalidOperationException>(() =>
            SettingsEncryptionHelper.EnsureEncryptionKeyConfigured(isDevelopment: false, resolvedKey: FallbackKey));

    [Fact]
    public void AllowsFallbackKeyInDevelopment() =>
        SettingsEncryptionHelper.EnsureEncryptionKeyConfigured(isDevelopment: true, resolvedKey: FallbackKey);

    [Fact]
    public void AllowsRealKeyOutsideDevelopment() =>
        SettingsEncryptionHelper.EnsureEncryptionKeyConfigured(isDevelopment: false, resolvedKey: "a-real-operator-configured-key");
}
