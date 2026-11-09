using Dapper;
using Microsoft.Data.Sqlite;
using PortwayApi.Services.Database;
using PortwayApi.Tests.Support;
using Xunit;

namespace PortwayApi.Tests.Services;

/// <summary>
/// Nightly pruning of auth failure audits by age and size
/// </summary>
public sealed class AuditRetentionTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 9, 3, 0, 0, DateTimeKind.Utc);
    private readonly TempDirectory _dir = new("portway_audit_retention");
    private readonly SqliteConnection _connection;

    public AuditRetentionTests()
    {
        _connection = new SqliteConnection($"Data Source={Path.Combine(_dir.Path, "auth.db")};Pooling=False");
        _connection.Open();
        _connection.Execute("""
            CREATE TABLE TokenAudits (Id INTEGER PRIMARY KEY AUTOINCREMENT, TokenId INTEGER NULL, Username TEXT NOT NULL DEFAULT '',
                Operation TEXT NOT NULL DEFAULT '', OldTokenHash TEXT NULL, NewTokenHash TEXT NULL, Timestamp DATETIME DEFAULT CURRENT_TIMESTAMP,
                Details TEXT NOT NULL DEFAULT '', Source TEXT NOT NULL DEFAULT 'PortwayApi', IpAddress TEXT NULL, UserAgent TEXT NULL)
            """);
    }

    public void Dispose()
    {
        _connection.Dispose();
        _dir.Dispose();
    }

    private void Add(string operation, int daysAgo, int detailBytes = 10) =>
        _connection.Execute("INSERT INTO TokenAudits (Operation, Timestamp, Details) VALUES (@operation, @timestamp, @details)",
            new { operation, timestamp = Now.AddDays(-daysAgo).ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF"), details = new string('x', detailBytes) });

    private List<(string Operation, long Id)> Rows() =>
        _connection.Query<(string, long)>("SELECT Operation, Id FROM TokenAudits ORDER BY Id").ToList();

    private Task<int> Prune(int days, int maxSizeMb) =>
        DatabaseMaintenanceService.PruneAuthFailureAuditsAsync(_connection,
            new AuditRetentionOptions { Days = days, MaxSizeMb = maxSizeMb }, Now, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Days_DeletesOlderFailureRows_AndRetainsLifecycleRows()
    {
        Add("FailedAuth", 200);
        Add("AuthorizationFailed", 181);
        Add("Created", 400);
        Add("FailedAuth", 10);

        var removed = await Prune(180, 0);

        Assert.Equal(2, removed);
        Assert.Equal(["Created", "FailedAuth"], Rows().Select(r => r.Operation));
    }

    [Fact]
    public async Task MaxSize_DeletesOldestFailureRows_UntilUnderTheLimit()
    {
        Add("Created", 1, 2_000_000);
        for (var i = 0; i < 10; i++)
            Add(i % 2 == 0 ? "FailedAuth" : "AuthorizationFailed", 1, 300_000);

        await Prune(0, 1);

        var rows = Rows();
        Assert.Equal("Created", rows[0].Operation);
        Assert.Equal([9L, 10L, 11L], rows.Skip(1).Select(r => r.Id));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-5, -1)]
    public async Task ZeroOrNegative_DisablesPruning(int days, int maxSizeMb)
    {
        Add("FailedAuth", 1000, 2_000_000);

        Assert.Equal(0, await Prune(days, maxSizeMb));
        Assert.Single(Rows());
    }

    [Fact]
    public void Defaults_AreSixMonthsAndNoSizeLimit()
    {
        var options = new DatabaseMaintenanceOptions().AuditRetention;

        Assert.Equal(180, options.Days);
        Assert.Equal(0, options.MaxSizeMb);
    }
}
