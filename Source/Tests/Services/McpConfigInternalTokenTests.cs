using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PortwayApi.Services.Mcp;
using Xunit;

namespace PortwayApi.Tests.Services;

/// <summary>
/// The removed internal chat token is purged from mcp.db on start
/// </summary>
public sealed class McpConfigInternalTokenTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"portway_mcp_{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task StartupDeletesStoredInternalToken()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = new McpConfigDbContext(new DbContextOptionsBuilder<McpConfigDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        db.EnsureTablesCreated();
        db.Config.AddRange(
            new McpConfigEntry { Key = "InternalApiToken", Value = "PWENC:secret", IsEncrypted = true },
            new McpConfigEntry { Key = "Provider", Value = "anthropic" });
        await db.SaveChangesAsync(ct);

        db.EnsureTablesCreated();

        var keys = await db.Config.AsNoTracking().Select(e => e.Key).ToListAsync(ct);
        Assert.Equal(["Provider"], keys);
    }
}
