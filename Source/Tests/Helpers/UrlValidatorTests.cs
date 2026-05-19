using System.IO;
using System.Text.Json;
using PortwayApi.Helpers;
using Xunit;

namespace PortwayApi.Tests.Helpers;

public class UrlValidatorTests : IDisposable
{
    private readonly string _configPath;

    public UrlValidatorTests()
    {
        _configPath = Path.GetTempFileName();
    }

    public void Dispose() => File.Delete(_configPath);

    private UrlValidator CreateValidator(string[] allowedHosts)
    {
        // Provide 3+ hosts so DiscoverAllowedHosts() is skipped (the condition is <= 2)
        var hosts = allowedHosts.Length >= 3
            ? allowedHosts
            : allowedHosts.Concat(["_noop1_", "_noop2_"]).ToArray();

        File.WriteAllText(_configPath, JsonSerializer.Serialize(new
        {
            AllowedHosts = hosts,
            BlockedIpRanges = Array.Empty<string>()
        }));

        return new UrlValidator(_configPath);
    }

    [Fact]
    public void IsHostAllowed_WildcardPattern_MatchesSingleSegment()
    {
        var validator = CreateValidator(["*.internal.test", "_noop1_", "_noop2_"]);
        Assert.True(validator.IsHostAllowed("svc.internal.test"));
    }

    [Fact]
    public void IsHostAllowed_ExactMatch_ReturnsTrue()
    {
        var validator = CreateValidator(["myhost.test", "_noop1_", "_noop2_"]);
        Assert.True(validator.IsHostAllowed("myhost.test"));
    }

    [Fact]
    public void IsHostAllowed_CaseInsensitive_ReturnsTrue()
    {
        var validator = CreateValidator(["MyHost.Test", "_noop1_", "_noop2_"]);
        Assert.True(validator.IsHostAllowed("myhost.test"));
        Assert.True(validator.IsHostAllowed("MYHOST.TEST"));
    }

    [Fact]
    public void IsHostAllowed_SameHostCalledTwice_ConsistentResult()
    {
        var validator = CreateValidator(["*.internal.test", "_noop1_", "_noop2_"]);
        var first = validator.IsHostAllowed("svc.internal.test");
        var second = validator.IsHostAllowed("svc.internal.test");
        Assert.Equal(first, second);
    }

    [Fact]
    public void IsHostAllowed_MultipleWildcardPatterns_EachMatchesCorrectly()
    {
        var validator = CreateValidator(["*.prod.test", "*.dev.test", "local.test"]);
        Assert.True(validator.IsHostAllowed("app.prod.test"));
        Assert.True(validator.IsHostAllowed("api.prod.test"));
        Assert.True(validator.IsHostAllowed("app.dev.test"));
        Assert.True(validator.IsHostAllowed("local.test"));
    }

    [Fact]
    public void IsHostAllowed_WildcardPattern_DifferentPrefixesAllMatch()
    {
        var validator = CreateValidator(["*.internal.test", "_noop1_", "_noop2_"]);
        Assert.True(validator.IsHostAllowed("svc-a.internal.test"));
        Assert.True(validator.IsHostAllowed("svc-b.internal.test"));
        Assert.True(validator.IsHostAllowed("api.internal.test"));
    }
}
