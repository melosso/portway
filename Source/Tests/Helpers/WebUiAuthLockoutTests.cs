using PortwayApi.Helpers;
using Xunit;

namespace PortwayApi.Tests.Helpers;

/// <summary>
/// The console sign-in lockout applies under parallel attempts and is scoped per client and account
/// </summary>
public class WebUiAuthLockoutTests
{
    private static string NewIp() => $"198.51.100.{Random.Shared.Next(1, 255)}-{Guid.NewGuid():N}";

    [Fact]
    public async Task ParallelAttempts_OnlyTenAllowed()
    {
        var ip = NewIp();

        var results = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => Task.Run(() => WebUiAuthHelper.BeginAttempt(ip, "admin"))));

        Assert.Equal(10, results.Count(r => r is null));
    }

    [Fact]
    public void SuccessClearsCount()
    {
        var ip = NewIp();
        for (var i = 0; i < 9; i++)
            Assert.Null(WebUiAuthHelper.BeginAttempt(ip, "admin"));

        WebUiAuthHelper.ClearFailedAttempts(ip, "admin");

        for (var i = 0; i < 10; i++)
            Assert.Null(WebUiAuthHelper.BeginAttempt(ip, "admin"));
    }

    [Fact]
    public void SuccessOnOtherAccount_DoesNotResetLockout()
    {
        var ip = NewIp();
        for (var i = 0; i < 9; i++)
            Assert.Null(WebUiAuthHelper.BeginAttempt(ip, "victim"));

        Assert.Null(WebUiAuthHelper.BeginAttempt(ip, "attacker"));
        WebUiAuthHelper.ClearFailedAttempts(ip, "attacker");

        Assert.Null(WebUiAuthHelper.BeginAttempt(ip, "victim"));
        Assert.NotNull(WebUiAuthHelper.BeginAttempt(ip, "victim"));
    }

    [Fact]
    public void AccountCount_IgnoresUsernameCase()
    {
        var ip = NewIp();
        for (var i = 0; i < 10; i++)
            WebUiAuthHelper.BeginAttempt(ip, i % 2 == 0 ? "admin" : "ADMIN");

        Assert.NotNull(WebUiAuthHelper.BeginAttempt(ip, "Admin"));
    }

    [Fact]
    public void LockedAccount_DoesNotBlockOtherAccountFromSameClient()
    {
        var ip = NewIp();
        for (var i = 0; i < 10; i++)
            WebUiAuthHelper.BeginAttempt(ip, "bne");

        Assert.NotNull(WebUiAuthHelper.BeginAttempt(ip, "bne"));
        Assert.Null(WebUiAuthHelper.BeginAttempt(ip, "admin"));
    }

    [Fact]
    public void ClientCeiling_BlocksSprayingAcrossAccounts()
    {
        var ip = NewIp();
        for (var i = 0; i < 50; i++)
            Assert.Null(WebUiAuthHelper.BeginAttempt(ip, $"user{i}"));

        Assert.NotNull(WebUiAuthHelper.BeginAttempt(ip, "another"));
    }

    [Fact]
    public void Successes_DoNotConsumeClientCeiling()
    {
        var ip = NewIp();
        for (var i = 0; i < 100; i++)
        {
            Assert.Null(WebUiAuthHelper.BeginAttempt(ip, $"user{i % 5}"));
            WebUiAuthHelper.ClearFailedAttempts(ip, $"user{i % 5}");
        }
    }

    [Fact]
    public void AccountKey_HasOneSizeForAnyUsername()
    {
        var shortKey = WebUiAuthHelper.AccountKey("203.0.113.1", "admin");
        var hugeKey = WebUiAuthHelper.AccountKey("203.0.113.1", new string('x', 10_000_000));

        Assert.Equal(shortKey.Length, hugeKey.Length);
        Assert.True(hugeKey.Length < 128);
    }

    [Fact]
    public void AccountKey_IgnoresCaseAndSeparatesAccounts()
    {
        Assert.Equal(WebUiAuthHelper.AccountKey("203.0.113.1", "Admin"), WebUiAuthHelper.AccountKey("203.0.113.1", "admin"));
        Assert.NotEqual(WebUiAuthHelper.AccountKey("203.0.113.1", "admin"), WebUiAuthHelper.AccountKey("203.0.113.1", "viewer"));
    }
}
