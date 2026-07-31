namespace PortwayApi.Tests.Services;

using Microsoft.Extensions.Options;
using PortwayApi.Services.Caching;
using Xunit;

/// <summary>MemoryCacheSizeLimitMB is a byte budget, not an item count</summary>
public class MemoryCacheBudgetTests
{
    private static MemoryCacheProvider BuildProvider(int budgetMb) =>
        new(Options.Create(new CacheOptions { MemoryCacheSizeLimitMB = budgetMb }));

    [Fact]
    public async Task Payload_LargerThanBudget_IsNotCached()
    {
        var provider = BuildProvider(budgetMb: 1);
        var twoMegabytes = new byte[2 * 1024 * 1024];

        await provider.SetAsync("static:big", twoMegabytes, TimeSpan.FromMinutes(5));

        Assert.Null(await provider.GetAsync<byte[]>("static:big"));
    }

    [Fact]
    public async Task Payload_WithinBudget_IsCached()
    {
        var provider = BuildProvider(budgetMb: 1);
        var oneKilobyte = new byte[1024];

        await provider.SetAsync("static:small", oneKilobyte, TimeSpan.FromMinutes(5));

        Assert.NotNull(await provider.GetAsync<byte[]>("static:small"));
    }

    // Objects have no Length to read, they are charged their serialized byte count
    [Fact]
    public async Task ObjectPayload_LargerThanBudget_IsNotCached()
    {
        var provider = BuildProvider(budgetMb: 1);
        var entry = ProxyCacheEntry.Create(new string('x', 2 * 1024 * 1024), [], 200);

        await provider.SetAsync("proxy:big", entry, TimeSpan.FromMinutes(5));

        Assert.Null(await provider.GetAsync<ProxyCacheEntry>("proxy:big"));
    }
}
