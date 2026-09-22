using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PortwayApi.Auth;
using Xunit;

namespace PortwayApi.Tests.Auth;

/// <summary>
/// PUT must apply the same last-full-access-token guard as DELETE already does
/// </summary>
public class TokenServiceLastFullAccessGuardTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AuthDbContext _db;
    private readonly TokenService _tokenService;

    public TokenServiceLastFullAccessGuardTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"portway_lastfullaccess_{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        _db = new AuthDbContext(options);
        _db.Database.EnsureCreated();

        var cache = new TokenVerificationCache(new MemoryCache(new MemoryCacheOptions()));
        _tokenService = new TokenService(_db, cache);
    }

    public void Dispose()
    {
        _db.Dispose();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private async Task<int> SeedTokenAsync(string username, string scopes, string environments)
    {
        var token = new AuthToken
        {
            Username = username,
            TokenHash = "hash",
            TokenSalt = "salt",
            AllowedScopes = scopes,
            AllowedEnvironments = environments
        };
        _db.Tokens.Add(token);
        await _db.SaveChangesAsync();
        return token.Id;
    }

    [Fact]
    public async Task NarrowingScopesOnTheSoleFullAccessToken_IsRefused()
    {
        var id = await SeedTokenAsync("solo-admin", "*", "*");

        var ok = await _tokenService.UpdateTokenScopesAsync(id, "Products");

        Assert.False(ok);
        var token = await _db.Tokens.FindAsync(id);
        Assert.Equal("*", token!.AllowedScopes);
    }

    [Fact]
    public async Task NarrowingEnvironmentsOnTheSoleFullAccessToken_IsRefused()
    {
        var id = await SeedTokenAsync("solo-admin", "*", "*");

        var ok = await _tokenService.UpdateTokenEnvironmentsAsync(id, "500");

        Assert.False(ok);
        var token = await _db.Tokens.FindAsync(id);
        Assert.Equal("*", token!.AllowedEnvironments);
    }

    [Fact]
    public async Task NarrowingScopes_IsAllowedWhenAnotherFullAccessTokenExists()
    {
        var id = await SeedTokenAsync("admin-one", "*", "*");
        await SeedTokenAsync("admin-two", "*", "*");

        var ok = await _tokenService.UpdateTokenScopesAsync(id, "Products");

        Assert.True(ok);
        var token = await _db.Tokens.FindAsync(id);
        Assert.Equal("Products", token!.AllowedScopes);
    }

    [Fact]
    public async Task NarrowingScopes_IsAllowedWhenTokenIsAlreadyNotFullAccess()
    {
        var id = await SeedTokenAsync("scoped-user", "Products", "*");

        var ok = await _tokenService.UpdateTokenScopesAsync(id, "Customers");

        Assert.True(ok);
        var token = await _db.Tokens.FindAsync(id);
        Assert.Equal("Customers", token!.AllowedScopes);
    }
}
