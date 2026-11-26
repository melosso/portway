using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PortwayApi.Endpoints;
using PortwayApi.Auth;
using PortwayApi.Helpers;
using PortwayApi.Services.Avatars;
using PortwayApi.Startup;
using Xunit;

namespace PortwayApi.Tests.Auth;

public sealed class NewAccountAvatarTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"portway_avatar_{Guid.NewGuid():N}.db");
    private AuthDbContext _db = null!;

    public async ValueTask InitializeAsync()
    {
        _db = new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        await _db.Database.EnsureCreatedAsync();
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
        return ValueTask.CompletedTask;
    }

    private static void AssertRandomAvatar(AdminUser user)
    {
        Assert.True(AvatarService.IsKnownStyle(user.AvatarStyle));
        Assert.True(AvatarService.IsValidSeed(user.AvatarSeed));
        Assert.NotEqual(user.Username, user.AvatarSeed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("demo-seed-password")]
    public async Task SeededFirstAccount_GetsARandomAvatar(string? seedPassword)
    {
        await new AdminUserService(_db).SeedFirstAccountAsync(new AdminSeedKey(null), seedPassword);
        AssertRandomAvatar(await _db.AdminUsers.SingleAsync());
    }

    [Fact]
    public async Task CliAccount_GetsARandomAvatar()
    {
        Assert.Equal(0, await AccountsCli.CreateAsync(_db, "cli-user", "Cli-Pass-2026!long", AdminUserRoles.Viewer));
        AssertRandomAvatar(await _db.AdminUsers.SingleAsync());
    }

    [Fact]
    public void OidcAccount_GetsARandomAvatar()
    {
        var identity = new OidcIdentity("subject-1", "oidc-user", "oidc@example.com", true, []);
        AssertRandomAvatar(WebUiEndpointExtensions.Provision(_db, new OidcProvider { Slug = "test" }, identity));
    }
}
