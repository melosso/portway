using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using PortwayApi.Services.Caching;
using PortwayApi.Services.Health;
using PortwayApi.Tests.Parity;
using Testcontainers.Redis;
using Xunit;

namespace PortwayApi.Tests.Services;

public class RedisCacheHealthCheckTests
{
    private static IOptions<CacheOptions> RedisOptions(string connectionString) => Options.Create(new CacheOptions
    {
        ProviderType = CacheProviderType.Redis,
        Redis = new RedisOptions { ConnectionString = connectionString }
    });

    [DockerFact]
    public async Task ReportsHealthy_AgainstReachableRedis_WithoutAdminMode()
    {
        await using var container = new RedisBuilder("redis:8-alpine").Build();
        await container.StartAsync();

        using var check = new RedisCacheHealthCheck(RedisOptions(container.GetConnectionString()));
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public void ResolvesAsSingleton()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Caching:ProviderType"] = "Redis",
                ["Caching:Redis:ConnectionString"] = "127.0.0.1:1,connectTimeout=100"
            })
            .Build();

        using var provider = new ServiceCollection().AddCachingServices(configuration).BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<RedisCacheHealthCheck>(), provider.GetRequiredService<RedisCacheHealthCheck>());
    }
}
