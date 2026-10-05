using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PortwayApi.Auth;
using Xunit;

namespace PortwayApi.Tests.Auth;

/// <summary>
/// Credential checks run the full hash even when the account is missing or has no local password, so response time does not reveal which usernames exist.
/// </summary>
public sealed class LoginTimingTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"portway_timing_{Guid.NewGuid():N}.db");
    private AuthDbContext _db = null!;
    private AdminUserService _users = null!;

    public async ValueTask InitializeAsync()
    {
        _db = new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        await _db.Database.EnsureCreatedAsync();
        _users = new AdminUserService(_db);
        await _users.CreateAsync("real-user", "correct-horse-battery", AdminUserRoles.Administrator);
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
        return ValueTask.CompletedTask;
    }

    private static async Task<double> MedianMillisAsync(Func<Task> action)
    {
        var samples = new List<double>();
        for (var i = 0; i < 7; i++)
        {
            var sw = Stopwatch.StartNew();
            await action();
            sw.Stop();
            samples.Add(sw.Elapsed.TotalMilliseconds);
        }
        samples.Sort();
        return samples[samples.Count / 2];
    }

    [Fact]
    public async Task MissingUsernameTakesAsLongAsWrongPassword()
    {
        var real = await MedianMillisAsync(() => _users.AuthenticateAsync("real-user", "wrong-password"));
        var missing = await MedianMillisAsync(() => _users.AuthenticateAsync("no-such-user", "wrong-password"));

        Assert.True(missing >= real * 0.5,
            $"missing-user auth ({missing:F1}ms) short-circuits the hash of a real-user auth ({real:F1}ms); timing reveals valid usernames");
    }

    [Fact]
    public async Task PasswordChangeWithMissingUsernameTakesAsLongAsWrongPassword()
    {
        var real = await MedianMillisAsync(async () =>
            await _users.ChangePasswordAsync("real-user", "wrong-password", "another-strong-pass"));
        var missing = await MedianMillisAsync(async () =>
            await _users.ChangePasswordAsync("no-such-user", "wrong-password", "another-strong-pass"));

        Assert.True(missing >= real * 0.5,
            $"missing-user change ({missing:F1}ms) short-circuits the hash of a real-user change ({real:F1}ms); timing reveals valid usernames");
    }
}
