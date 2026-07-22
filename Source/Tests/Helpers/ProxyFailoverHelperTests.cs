using PortwayApi.Helpers;
using Xunit;

namespace PortwayApi.Tests.Helpers;

public class ProxyFailoverHelperTests
{
    [Fact]
    public void BuildCandidateUrls_NoFallbacks_ReturnsPrimaryOnly()
    {
        var result = ProxyFailoverHelper.BuildCandidateUrls(
            "http://primary/api/items?x=1", "http://primary/api", null);

        Assert.Single(result);
        Assert.Equal("http://primary/api/items?x=1", result[0]);
    }

    [Fact]
    public void BuildCandidateUrls_WithFallbacks_RebasesSuffix()
    {
        var result = ProxyFailoverHelper.BuildCandidateUrls(
            "http://primary/api/items(42)?x=1", "http://primary/api",
            new List<string> { "http://standby/api" });

        Assert.Equal(2, result.Count);
        Assert.Equal("http://standby/api/items(42)?x=1", result[1]);
    }

    [Fact]
    public void BuildCandidateUrls_BaseWithTrailingSlash_RebasesCorrectly()
    {
        var result = ProxyFailoverHelper.BuildCandidateUrls(
            "http://primary/api/items", "http://primary/api/",
            new List<string> { "http://standby/api/" });

        Assert.Equal("http://standby/api/items", result[1]);
    }

    [Fact]
    public void BuildCandidateUrls_PrefixMismatch_ReturnsPrimaryOnly()
    {
        var result = ProxyFailoverHelper.BuildCandidateUrls(
            "http://rewritten/other/items", "http://primary/api",
            new List<string> { "http://standby/api" });

        Assert.Single(result);
    }

    [Fact]
    public void BuildCandidateUrls_BlankFallbacksSkipped()
    {
        var result = ProxyFailoverHelper.BuildCandidateUrls(
            "http://primary/api/items", "http://primary/api",
            new List<string> { "", "  ", "http://standby/api" });

        Assert.Equal(2, result.Count);
    }

    [Theory]
    [InlineData(502, true)]
    [InlineData(503, true)]
    [InlineData(504, true)]
    [InlineData(500, false)]
    [InlineData(404, false)]
    [InlineData(200, false)]
    public void IsTransientStatus_ClassifiesCorrectly(int statusCode, bool expected)
        => Assert.Equal(expected, ProxyFailoverHelper.IsTransientStatus(statusCode));
}
