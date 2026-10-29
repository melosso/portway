using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PortwayApi.Auth;
using Xunit;

namespace PortwayApi.Tests.Auth;

/// <summary>
/// Token tenant grants in auth.db and the in-place column upgrade
/// </summary>
public sealed class TokenTenantTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"portway_tenants_{Guid.NewGuid():N}.db");
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
    public async Task GrantsStoredCanonical()
    {
        var token = await _tokens.GenerateTokenAsync($"t-{Guid.NewGuid():N}", allowedTenants: """{"X-Company-Id":["GLOBEX","ACME"]}""", ct: TestContext.Current.CancellationToken);

        var details = await _tokens.GetTokenDetailsByTokenAsync(token);

        Assert.Equal("""{"X-Company-Id":["ACME","GLOBEX"]}""", details!.AllowedTenants);
        Assert.Contains("GLOBEX", (IReadOnlySet<string>)details.Tenants["x-company-id"]);
    }

    [Fact]
    public async Task InvalidGrantsRejected() =>
        await Assert.ThrowsAsync<ArgumentException>(() => _tokens.GenerateTokenAsync($"t-{Guid.NewGuid():N}", allowedTenants: """{"Authorization":["x"]}""", ct: TestContext.Current.CancellationToken));

    [Fact]
    public async Task UpdateReplacesGrants()
    {
        var token = await _tokens.GenerateTokenAsync($"t-{Guid.NewGuid():N}", allowedTenants: """{"X-Company-Id":["ACME"]}""", ct: TestContext.Current.CancellationToken);
        var id = (await _tokens.GetTokenDetailsByTokenAsync(token))!.Id;
        Assert.True(TenantGrants.TryParse("""{"X-Client-Id":["7"]}""", out var grants, out _));

        await _tokens.UpdateTokenTenantsAsync(id, grants, TestContext.Current.CancellationToken);

        var details = await _tokens.GetTokenDetailsByTokenAsync(token);
        Assert.False(details!.Tenants.ContainsKey("X-Company-Id"));
        Assert.Contains("7", (IReadOnlySet<string>)details.Tenants["X-Client-Id"]);
    }

    [Fact]
    public async Task ScopeAndEnvironmentUpdatesTakeEffectImmediately()
    {
        var token = await _tokens.GenerateTokenAsync($"t-{Guid.NewGuid():N}", allowedScopes: "Orders,Items", allowedEnvironments: "prod,dev", ct: TestContext.Current.CancellationToken);
        var details = (await _tokens.GetTokenDetailsByTokenAsync(token))!;
        var expiry = DateTime.UtcNow.AddDays(1);

        await _tokens.UpdateTokenScopesAsync(details.Id, "Orders", TestContext.Current.CancellationToken);
        Assert.Equal("Orders", (await _tokens.GetTokenDetailsByTokenAsync(token))!.AllowedScopes);

        await _tokens.UpdateTokenEnvironmentsAsync(details.Id, "dev", TestContext.Current.CancellationToken);
        Assert.Equal("dev", (await _tokens.GetTokenDetailsByTokenAsync(token))!.AllowedEnvironments);

        await _tokens.SetTokenExpirationAsync(details.Id, expiry, TestContext.Current.CancellationToken);
        Assert.Equal(expiry, (await _tokens.GetTokenDetailsByTokenAsync(token))!.ExpiresAt);
    }

    [Fact]
    public void OldDatabaseGetsColumn()
    {
        var path = Path.Combine(Path.GetTempPath(), $"portway_oldauth_{Guid.NewGuid():N}.db");
        try
        {
            using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                connection.Execute("""
                    CREATE TABLE Tokens (Id INTEGER PRIMARY KEY AUTOINCREMENT, Username TEXT NOT NULL, TokenHash TEXT NOT NULL, TokenSalt TEXT NOT NULL,
                        CreatedAt DATETIME, RevokedAt DATETIME NULL, ExpiresAt DATETIME NULL, AllowedScopes TEXT NOT NULL DEFAULT '*',
                        AllowedEnvironments TEXT NOT NULL DEFAULT '*', Description TEXT NOT NULL DEFAULT '');
                    INSERT INTO Tokens (Username, TokenHash, TokenSalt, CreatedAt) VALUES ('legacy', 'h', 's', CURRENT_TIMESTAMP);
                    """);
            }

            using var db = new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>().UseSqlite($"Data Source={path}").Options);
            db.EnsureTablesCreated();

            var legacy = db.Tokens.AsNoTracking().Single();
            Assert.Equal(TenantGrants.Empty, legacy.AllowedTenants);
            Assert.Empty(legacy.Tenants);
            Assert.Null(legacy.TokenLookup);
            Assert.Equal(1, db.Database.GetDbConnection().ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='IX_AuthTokens_TokenLookup'"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }
}
