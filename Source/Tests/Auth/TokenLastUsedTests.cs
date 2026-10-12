using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PortwayApi.Auth;
using Xunit;

namespace PortwayApi.Tests.Auth;

/// <summary>
/// A verified token records when it was last used
/// </summary>
public sealed class TokenLastUsedTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"portway_lastused_{Guid.NewGuid():N}.db");
    private AuthDbContext _db = null!;
    private TokenService _tokens = null!;

    public async ValueTask InitializeAsync()
    {
        _db = new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        await _db.Database.EnsureCreatedAsync();
        _tokens = new TokenService(_db, new TokenVerificationCache(new MemoryCache(new MemoryCacheOptions())));
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task NewTokenIsNeverUsed()
    {
        var username = $"t-{Guid.NewGuid():N}";
        await _tokens.GenerateTokenAsync(username, ct: TestContext.Current.CancellationToken);

        var stored = await _db.Tokens.AsNoTracking().SingleAsync(t => t.Username == username, TestContext.Current.CancellationToken);

        Assert.Null(stored.LastUsedAt);
    }

    [Fact]
    public async Task VerifiedTokenRecordsLastUse()
    {
        var username = $"t-{Guid.NewGuid():N}";
        var token = await _tokens.GenerateTokenAsync(username, ct: TestContext.Current.CancellationToken);
        var before = DateTime.UtcNow.AddSeconds(-1);

        await _tokens.GetTokenDetailsByTokenAsync(token);

        var stored = await _db.Tokens.AsNoTracking().SingleAsync(t => t.Username == username, TestContext.Current.CancellationToken);
        Assert.NotNull(stored.LastUsedAt);
        Assert.InRange(stored.LastUsedAt.Value, before, DateTime.UtcNow.AddSeconds(1));
    }

    [Fact]
    public async Task InvalidTokenRecordsNothing()
    {
        var username = $"t-{Guid.NewGuid():N}";
        await _tokens.GenerateTokenAsync(username, ct: TestContext.Current.CancellationToken);

        await _tokens.GetTokenDetailsByTokenAsync("not-a-token");

        var stored = await _db.Tokens.AsNoTracking().SingleAsync(t => t.Username == username, TestContext.Current.CancellationToken);
        Assert.Null(stored.LastUsedAt);
    }
}
