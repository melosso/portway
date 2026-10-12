using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PortwayApi.Services.Mcp;
using Xunit;

namespace PortwayApi.Tests.Services;

/// <summary>
/// A cleared chat config is not resurrected by a read that overlapped the clear
/// </summary>
public sealed class McpConfigCacheTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"portway_mcpcache_{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task ClearDuringReads_LeavesNoKeyCached()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<McpConfigDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        await using (var db = new McpConfigDbContext(options))
            db.EnsureTablesCreated();
        var service = new McpConfigService(new PooledDbContextFactory<McpConfigDbContext>(options));

        for (var round = 0; round < 30; round++)
        {
            await service.SaveConfigAsync("anthropic", "model", "secret", ct);
            var readers = Enumerable.Range(0, 8).Select(_ => Task.Run(() => service.GetConfigAsync(ct), ct)).ToList();
            await service.ClearConfigAsync(ct);
            await Task.WhenAll(readers);

            Assert.Null((await service.GetConfigAsync(ct)).ApiKey);
        }
    }
}
