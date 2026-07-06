using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Moq;
using PortwayApi.Services.Configuration;
using PortwayApi.Services.Database;
using Xunit;

namespace PortwayApi.Tests.Services;

public class DatabaseMaintenanceServiceTests : IDisposable
{
    private readonly string _dir;

    public DatabaseMaintenanceServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"portway_maint_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private DatabaseMaintenanceService CreateService(DatabaseMaintenanceOptions options, string trafficPath)
    {
        var monitor = new Mock<IOptionsMonitor<DatabaseMaintenanceOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(options);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RequestTrafficLogging:SqlitePath"] = trafficPath
        }).Build();
        return new DatabaseMaintenanceService(monitor.Object, config, new ConfigAuditService());
    }

    private static void CreateBloatedDb(string path)
    {
        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE t (id INTEGER PRIMARY KEY, payload TEXT)";
            cmd.ExecuteNonQuery();
        }
        using (var tx = conn.BeginTransaction())
        {
            using var insert = conn.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = "INSERT INTO t (payload) VALUES ($p)";
            var p = insert.Parameters.Add("$p", SqliteType.Text);
            for (var i = 0; i < 2000; i++)
            {
                p.Value = new string('x', 512);
                insert.ExecuteNonQuery();
            }
            tx.Commit();
        }
        using (var cmd = conn.CreateCommand())
        {
            // Delete most rows so the freelist grows well past the vacuum threshold
            cmd.CommandText = "DELETE FROM t WHERE id > 10";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task RunOnce_VacuumsBloatedDatabase_AndRefreshesStatistics()
    {
        var dbPath = Path.Combine(_dir, "traffic_logs.db");
        CreateBloatedDb(dbPath);
        var sizeBefore = new FileInfo(dbPath).Length;

        var service = CreateService(new DatabaseMaintenanceOptions { FreePageRatioThreshold = 0.25 }, dbPath);
        var results = await service.RunOnceAsync();

        var result = Assert.Single(results, r => r.Database == "traffic_logs.db");
        Assert.True(result.Analyzed);
        Assert.True(result.Vacuumed);
        Assert.True(new FileInfo(dbPath).Length < sizeBefore);
        Assert.NotNull(service.LastRunUtc);

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name = 'sqlite_stat1'";
        Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task RunOnce_SkipsVacuum_WhenBelowThreshold()
    {
        var dbPath = Path.Combine(_dir, "traffic_logs.db");
        using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE t (id INTEGER PRIMARY KEY); INSERT INTO t (id) VALUES (1)";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        var service = CreateService(new DatabaseMaintenanceOptions { FreePageRatioThreshold = 0.25 }, dbPath);
        var results = await service.RunOnceAsync();

        var result = Assert.Single(results, r => r.Database == "traffic_logs.db");
        Assert.True(result.Analyzed);
        Assert.False(result.Vacuumed);
        Assert.Contains("threshold", result.SkipReason);
    }

    [Fact]
    public async Task RunOnce_IgnoresMissingDatabases()
    {
        var service = CreateService(new DatabaseMaintenanceOptions(), Path.Combine(_dir, "missing.db"));
        var results = await service.RunOnceAsync();
        Assert.DoesNotContain(results, r => r.Database == "missing.db");
    }

    [Theory]
    [InlineData("03:00")]
    [InlineData("garbage")]
    [InlineData("25:99")]
    public void DelayUntilNextRun_AlwaysPositive_AndUnderOneDay(string schedule)
    {
        var delay = DatabaseMaintenanceService.DelayUntilNextRun(schedule);
        Assert.True(delay > TimeSpan.Zero);
        Assert.True(delay <= TimeSpan.FromDays(1));
    }
}
