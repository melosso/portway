namespace PortwayApi.Tests.Concurrency;

using System.Collections.Concurrent;
using System.Data;
using PortwayApi.Services;
using PortwayApi.Services.Providers;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// Load test of the pooled connect path against a real database, not a regression guard
/// </summary>
public class SqlConnectionPoolConcurrencyTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private string _connectionString = string.Empty;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();
    }

    public async ValueTask DisposeAsync() => await _postgres.DisposeAsync();

    private static SqlConnectionPoolService BuildPool() => new(
        new SqlPoolingOptions(
            MinPoolSize: 3,
            MaxPoolSize: 40,
            ConnectionTimeout: 15,
            EnablePooling: true,
            ApplicationName: "PortwayConcurrencyTest"),
        new SqlProviderFactory([new MsSqlProvider(), new PostgreSqlProvider(), new MySqlProvider(), new SqliteProvider()]));

    [Fact]
    public async Task ConcurrentConnections_UnderLoad_AllSucceed()
    {
        var pool = BuildPool();

        const int workers = 64;
        const int perWorker = 20;
        var failures = new ConcurrentBag<Exception>();
        var optimised = new ConcurrentBag<string>();

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


        Assert.True(failures.IsEmpty,
            $"{failures.Count} of {workers * perWorker} pooled connections failed:\n" +
            string.Join("\n", failures.Take(3).Select(e => $"{e.GetType().Name}: {e.Message}")));

        // The connection-string cache must hand every caller the identical optimised string
        Assert.Single(optimised.Distinct());
    }
}
