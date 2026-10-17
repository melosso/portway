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

    // Interleaved so CPU contention from parallel tests lands on both medians alike
    private static async Task<(double Real, double Missing)> MedianMillisAsync(Func<Task> real, Func<Task> missing)
    {
        var realSamples = new List<double>();
        var missingSamples = new List<double>();
        for (var i = 0; i < 7; i++)
        {
            realSamples.Add(await TimeAsync(real));
            missingSamples.Add(await TimeAsync(missing));
        }
        return (Median(realSamples), Median(missingSamples));
    }

    private static async Task<double> TimeAsync(Func<Task> action)
    {
        var sw = Stopwatch.StartNew();
        await action();
        return sw.Elapsed.TotalMilliseconds;
    }

    private static double Median(List<double> samples)
    {
        samples.Sort();
        return samples[samples.Count / 2];
    }

    [Fact]
    public async Task MissingUsernameTakesAsLongAsWrongPassword()
    {
        var (real, missing) = await MedianMillisAsync(
            () => _users.AuthenticateAsync("real-user", "wrong-password"),
            () => _users.AuthenticateAsync("no-such-user", "wrong-password"));

        Assert.True(missing >= real * 0.5,
            $"missing-user auth ({missing:F1}ms) short-circuits the hash of a real-user auth ({real:F1}ms); timing reveals valid usernames");
    }

    [Fact]
    public async Task PasswordChangeWithMissingUsernameTakesAsLongAsWrongPassword()
    {
        var (real, missing) = await MedianMillisAsync(
            () => _users.ChangePasswordAsync("real-user", "wrong-password", "another-strong-pass"),
            () => _users.ChangePasswordAsync("no-such-user", "wrong-password", "another-strong-pass"));

        Assert.True(missing >= real * 0.5,
            $"missing-user change ({missing:F1}ms) short-circuits the hash of a real-user change ({real:F1}ms); timing reveals valid usernames");
    }
}
