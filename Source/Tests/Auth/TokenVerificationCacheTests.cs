using Microsoft.Extensions.Caching.Memory;
using PortwayApi.Auth;
using Xunit;

namespace PortwayApi.Tests.Auth;

/// <summary>Verifies the negative-cache primitive that shields token verification from invalid-token floods</summary>
public class TokenVerificationCacheTests
{
    private static TokenVerificationCache CreateCache()
        => new(new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public void UnknownKey_IsNotAKnownMiss()
    {
        var cache = CreateCache();
        var key = TokenVerificationCache.BuildKey("some-token");

        Assert.False(cache.IsKnownMiss(key));
    }

    [Fact]
    public void SetMiss_ThenIsKnownMiss_ReturnsTrue()
    {
        var cache = CreateCache();
        var key = TokenVerificationCache.BuildKey("bad-token");

        cache.SetMiss(key);

        Assert.True(cache.IsKnownMiss(key));
    }

    [Fact]
    public void Miss_IsNamespacedAwayFromPositiveEntries()
    {
        var cache = CreateCache();
        var key = TokenVerificationCache.BuildKey("bad-token");

        cache.SetMiss(key);

        // A negative entry must not masquerade as a real cached token
        Assert.False(cache.TryGet(key, out _));
        Assert.True(cache.IsKnownMiss(key));
    }
}
