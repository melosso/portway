using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PortwayApi.Auth;
using Xunit;

namespace PortwayApi.Tests.Auth;

/// <summary>
/// Tokens are found by an indexed SHA-256 lookup; tokens stored before it existed still verify and are backfilled
/// </summary>
public sealed class TokenLookupTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"portway_lookup_{Guid.NewGuid():N}.db");
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

    private static string Sha256Hex(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    [Fact]
    public async Task NewTokenStoresSha256Lookup()
    {
        var username = $"t-{Guid.NewGuid():N}";
        var token = await _tokens.GenerateTokenAsync(username, ct: TestContext.Current.CancellationToken);

        var stored = await _db.Tokens.AsNoTracking().SingleAsync(t => t.Username == username, TestContext.Current.CancellationToken);
        Assert.Equal(Sha256Hex(token), stored.TokenLookup);
        Assert.Equal(stored.Id, (await _tokens.GetTokenDetailsByTokenAsync(token))?.Id);
    }

    [Fact]
    public async Task LegacyTokenVerifiesAndIsBackfilled()
    {
        const string token = "legacy-token-value";
        var salt = RandomNumberGenerator.GetBytes(16);
        _db.Tokens.Add(new AuthToken
        {
            Username = "legacy",
            TokenHash = Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(token, salt, 10000, HashAlgorithmName.SHA256, 32)),
            TokenSalt = Convert.ToBase64String(salt)
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(await _tokens.GetTokenDetailsByTokenAsync(token));

        var stored = await _db.Tokens.AsNoTracking().SingleAsync(t => t.Username == "legacy", TestContext.Current.CancellationToken);
        Assert.Equal(Sha256Hex(token), stored.TokenLookup);
    }

    [Fact]
    public async Task WrongTokenIsRejected()
    {
        await _tokens.GenerateTokenAsync($"t-{Guid.NewGuid():N}", ct: TestContext.Current.CancellationToken);

        Assert.Null(await _tokens.GetTokenDetailsByTokenAsync("not-a-token"));
    }
}
