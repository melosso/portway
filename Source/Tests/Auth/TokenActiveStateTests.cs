using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PortwayApi.Auth;
using Xunit;

namespace PortwayApi.Tests.Auth;

public sealed class TokenActiveStateTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"portway_tokenstate_{Guid.NewGuid():N}.db");
    private readonly AuthDbContext _db;

    public TokenActiveStateTests()
    {
        _db = new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    [Fact]
    public async Task ActiveQueryAndIsActive_AgreeOnEveryState()
    {
        var now = DateTime.UtcNow;
        AuthToken Token(string name, DateTime? expires = null, DateTime? revoked = null) =>
            new() { Username = name, TokenHash = "h", TokenSalt = "s", ExpiresAt = expires, RevokedAt = revoked };
        _db.Tokens.AddRange(
            Token("open"),
            Token("future", expires: now.AddDays(1)),
            Token("expired", expires: now.AddDays(-1)),
            Token("revoked", revoked: now.AddMinutes(-1)),
            Token("revoked-and-expired", expires: now.AddDays(-1), revoked: now.AddDays(-2)));
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var service = new TokenService(_db, new TokenVerificationCache(new MemoryCache(new MemoryCacheOptions())));
        var active = (await service.GetActiveTokensAsync()).Select(t => t.Username).Order().ToArray();
        var all = await _db.Tokens.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["future", "open"], active);
        Assert.Equal(active, all.Where(t => t.IsActive).Select(t => t.Username).Order().ToArray());
    }
}
