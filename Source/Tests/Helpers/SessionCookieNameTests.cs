using System.Security.Cryptography;
using PortwayApi.Helpers;
using Xunit;

namespace PortwayApi.Tests.Helpers;

/// <summary>
/// Console cookie names are unique per signing key so instances on one host keep separate sessions
/// </summary>
public class SessionCookieNameTests
{
    [Fact]
    public void CookieSuffix_DiffersPerKey()
    {
        var first = SessionKeyProvider.CookieSuffix(RandomNumberGenerator.GetBytes(32));
        var second = SessionKeyProvider.CookieSuffix(RandomNumberGenerator.GetBytes(32));

        Assert.NotEqual(first, second);
        Assert.Matches("^[0-9a-f]{8}$", first);
    }

    [Fact]
    public void CookieSuffix_IsStableForOneKey()
    {
        var key = RandomNumberGenerator.GetBytes(32);

        Assert.Equal(SessionKeyProvider.CookieSuffix(key), SessionKeyProvider.CookieSuffix(key));
    }

    [Fact]
    public void CookieNames_UseTheInstanceSuffix()
    {
        var suffix = SessionKeyProvider.CookieSuffix(SessionKeyProvider.Key);

        Assert.Equal($"portway_auth_{suffix}", SessionKeyProvider.SessionCookieName);
        Assert.Equal($"portway_csrf_{suffix}", SessionKeyProvider.CsrfCookieName);
    }
}
