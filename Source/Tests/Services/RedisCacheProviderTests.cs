using Microsoft.Extensions.Options;
using Moq;
using PortwayApi.Services.Caching;
using StackExchange.Redis;
using Xunit;

namespace PortwayApi.Tests.Services;

public sealed class RedisCacheProviderTests
{
    private sealed record Item(string Name);

    private static readonly RedisConnectionException ConnectionLost = new(ConnectionFailureType.SocketFailure, "connection lost");

    private static ConnectionFailedEventArgs NodeEvent() =>
        new(null, new System.Net.DnsEndPoint("redis", 6379), ConnectionType.Interactive, ConnectionFailureType.None, null, "redis");

    private static (RedisCacheProvider Provider, Mock<IDatabase> Db, Mock<IConnectionMultiplexer> Redis, MemoryCacheProvider Fallback) Create(bool fallback = true, bool connected = true)
    {
        var options = Options.Create(new CacheOptions { Redis = new RedisOptions { FallbackToMemoryCache = fallback, MaxRetryAttempts = 0 } });
        var db = new Mock<IDatabase>();
        var redis = new Mock<IConnectionMultiplexer>();
        redis.SetupGet(r => r.IsConnected).Returns(connected);
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object?>())).Returns(db.Object);
        var memory = new MemoryCacheProvider(options);
        return (new RedisCacheProvider(options, memory, redis.Object), db, redis, memory);
    }

    [Fact]
    public async Task ConnectionError_OnGet_ReturnsFallbackValue()
    {
        var (provider, db, _, memory) = Create();
        await memory.SetAsync("k", new Item("cached"), TimeSpan.FromMinutes(1));
        db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).ThrowsAsync(ConnectionLost);

        var value = await provider.GetAsync<Item>("k");

        Assert.Equal("cached", value?.Name);
    }

    [Fact]
    public async Task ConnectionError_WithoutFallback_IsACacheMiss()
    {
        var (provider, db, _, _) = Create(fallback: false);
        db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).ThrowsAsync(ConnectionLost);
        db.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).ThrowsAsync(ConnectionLost);

        Assert.Null(await provider.GetAsync<Item>("k"));
        await provider.RemoveAsync("k");
    }

    [Fact]
    public async Task CacheManager_UsesRedis_AfterAStartupWithoutConnection()
    {
        var (provider, db, redis, _) = Create(connected: false);
        var manager = new CacheManager(
            new PortwayApi.Tests.Support.StaticOptionsMonitor<CacheOptions>(new CacheOptions { ProviderType = CacheProviderType.Redis }),
            new MemoryCacheProvider(Options.Create(new CacheOptions())),
            new PortwayApi.Services.MetricsService(),
            new PortwayApi.Services.Telemetry.PortwayMetrics(),
            provider);

        redis.SetupGet(r => r.IsConnected).Returns(true);
        redis.Raise(r => r.ConnectionRestored += null, NodeEvent());
        await manager.GetAsync<Item>("k");

        db.Verify(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task OneFailedNode_KeepsUsingRedis_WhileTheMultiplexerIsConnected()
    {
        var (provider, db, redis, _) = Create();

        redis.Raise(r => r.ConnectionFailed += null, NodeEvent());
        await provider.GetAsync<Item>("k");

        Assert.True(provider.IsConnected);
        db.Verify(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()), Times.Once);
    }
}
