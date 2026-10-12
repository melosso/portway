using PortwayApi.Helpers;
using Xunit;

namespace PortwayApi.Tests.Helpers;

/// <summary>
/// The console sign-in lockout holds under parallel attempts
/// </summary>
public class WebUiAuthLockoutTests
{
    [Fact]
    public async Task ParallelAttempts_OnlyTenAllowed()
    {
        var ip = $"198.51.100.{Random.Shared.Next(1, 255)}-{Guid.NewGuid():N}";

        var results = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => Task.Run(() => WebUiAuthHelper.BeginAttempt(ip))));

        Assert.Equal(10, results.Count(r => r is null));
    }

    [Fact]
    public void SuccessClearsCount()
    {
        var ip = $"203.0.113.1-{Guid.NewGuid():N}";
        for (var i = 0; i < 9; i++)
            Assert.Null(WebUiAuthHelper.BeginAttempt(ip));

        WebUiAuthHelper.ClearFailedAttempts(ip);

        Assert.Null(WebUiAuthHelper.BeginAttempt(ip));
    }
}
