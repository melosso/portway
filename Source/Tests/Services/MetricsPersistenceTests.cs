using Microsoft.Data.Sqlite;
using PortwayApi.Services;
using Xunit;

namespace PortwayApi.Tests.Services;

public class MetricsPersistenceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"metrics-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task Startup_HydratesPersistedRequests()
    {
        var ct = TestContext.Current.CancellationToken;

        // Creates the schema
        var setup = new MetricsPersistenceService(new MetricsService(), _dbPath);
        await setup.StartAsync(ct);
        await setup.StopAsync(ct);

        await using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await conn.OpenAsync(ct);
            var insert = conn.CreateCommand();
            insert.CommandText = "INSERT INTO RequestMetrics (Timestamp, StatusCode, Method, Source, Endpoint, Environment, Version, DurationMs) VALUES ($ts, 200, 'GET', 'api', 'Orders', '500', '', 7)";
            insert.Parameters.AddWithValue("$ts", DateTime.UtcNow.AddMinutes(-5).ToString("o"));
            for (var i = 0; i < 3; i++)
                await insert.ExecuteNonQueryAsync(ct);
        }

        var metrics = new MetricsService();
        var service = new MetricsPersistenceService(metrics, _dbPath);
        await service.StartAsync(ct);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (metrics.GetHealth(TimeSpan.FromHours(1), new HealthFilter()).Summary.Total < 3 && DateTime.UtcNow < deadline)
            await Task.Delay(50, ct);
        await service.StopAsync(ct);

        Assert.Equal(3, metrics.GetHealth(TimeSpan.FromHours(1), new HealthFilter()).Summary.Total);
    }
}
