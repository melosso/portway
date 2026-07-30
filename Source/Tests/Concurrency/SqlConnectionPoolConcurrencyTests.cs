namespace PortwayApi.Tests.Concurrency;

using System.Collections.Concurrent;
using System.Data;
using PortwayApi.Services;
using PortwayApi.Services.Providers;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// Load test for the pooling path against a real database. This is a stress test, not a
/// regression guard: it exercises the connection-string cache, provider lookup and pooled
/// connect path under heavy parallelism rather than pinning one specific race.
/// </summary>
public class SqlConnectionPoolConcurrencyTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private static SqlConnectionPoolService BuildPool() => new(
        new SqlPoolingOptions(
            MinPoolSize: 3,
            MaxPoolSize: 40,
            ConnectionTimeout: 15,
            EnablePooling: true,
            ApplicationName: "PortwayConcurrencyTest"),
        new SqlProviderFactory([new MsSqlProvider(), new PostgreSqlProvider(), new MySqlProvider(), new SqliteProvider()]));

    // The host stops hosted services and the container disposes singletons, prevent `Cannot access a disposed object.` from happening again after pool is disposed
    [Fact]
    public async Task StopAsync_AfterDispose_DoesNotThrow()
    {
        var pool = BuildPool();
        await pool.PrewarmConnectionPoolAsync(_connectionString);
        await pool.StartAsync(CancellationToken.None);

        await pool.DisposeAsync();

        // Must not throw ObjectDisposedException on the maintenance gate
        await pool.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ConcurrentConnections_UnderLoad_AllSucceed()
    {
        await using var pool = BuildPool();
        await pool.PrewarmConnectionPoolAsync(_connectionString);
        await pool.StartAsync(CancellationToken.None);

        const int workers = 64;
        const int perWorker = 20;
        var failures = new ConcurrentBag<Exception>();
        var optimised = new ConcurrentBag<string>();

        // Maintenance runs against the same warmup connections while the load is in flight
        var maintenance = Task.Run(async () =>
        {
            for (var i = 0; i < 10; i++)
                await pool.MaintenanceTaskAsync();
        });

        await Parallel.ForEachAsync(
            Enumerable.Range(0, workers),
            new ParallelOptions { MaxDegreeOfParallelism = workers },
            async (_, ct) =>
            {
                for (var i = 0; i < perWorker; i++)
                {
                    try
                    {
                        optimised.Add(pool.OptimizeConnectionString(_connectionString));

                        await using var conn = pool.CreateConnection(_connectionString);
                        await conn.OpenAsync(ct);

                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = "SELECT 1";
                        Assert.Equal(1, Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)));
                        Assert.Equal(ConnectionState.Open, conn.State);
                    }
                    catch (Exception ex)
                    {
                        failures.Add(ex);
                    }
                }
            });

        await maintenance;
        await pool.StopAsync(CancellationToken.None);

        Assert.True(failures.IsEmpty,
            $"{failures.Count} of {workers * perWorker} pooled connections failed:\n" +
            string.Join("\n", failures.Take(3).Select(e => $"{e.GetType().Name}: {e.Message}")));

        // The connection-string cache must hand every caller the identical optimised string
        Assert.Single(optimised.Distinct());
    }
}
