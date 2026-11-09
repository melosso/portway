using System.Diagnostics;
using System.Net;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using PortwayApi.Auth;
using PortwayApi.Tests.Base;
using Xunit;

namespace PortwayApi.Tests.Middleware;

/// <summary>
/// Auth failures never wait on auth.db and are written at a bounded rate
/// </summary>
public sealed class AuthFailureAuditTests : ApiTestBase
{
    private int AuditCount(string operation)
    {
        using var connection = new SqliteConnection($"Data Source={_authDbPath};Pooling=False");
        return connection.ExecuteScalar<int>("SELECT count(*) FROM TokenAudits WHERE Operation = @operation", new { operation });
    }

    private async Task<int> WaitForAudits(string operation, int atLeast)
    {
        for (var attempt = 0; attempt < 50 && AuditCount(operation) < atLeast; attempt++)
            await Task.Delay(100, TestContext.Current.CancellationToken);
        return AuditCount(operation);
    }

    [Fact]
    public async Task FailedAuth_AnswersWhileAuthDbIsLocked()
    {
        AddAuthorizationHeader("not-a-token");
        using var warmup = await _client.GetAsync("/api/500/CRM/Accounts", TestContext.Current.CancellationToken);

        using (var locker = new SqliteConnection($"Data Source={_authDbPath};Pooling=False"))
        {
            locker.Open();
            locker.Execute("BEGIN EXCLUSIVE");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var watch = Stopwatch.StartNew();

            using var response = await _client.GetAsync("/api/500/CRM/Accounts", timeout.Token);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"401 took {watch.Elapsed}");
            locker.Execute("ROLLBACK");
        }
    }

    [Fact]
    public async Task FailedAuth_IsWrittenToTheAuditTrail()
    {
        AddAuthorizationHeader("not-a-token");

        using var response = await _client.GetAsync("/api/500/CRM/Accounts", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(await WaitForAudits("FailedAuth", 1) >= 1);
    }

    [Fact]
    public async Task Flood_WritesOneBatchPerInterval()
    {
        var queue = new AuthFailureAuditQueue(_factory.Services.GetRequiredService<IServiceScopeFactory>());
        for (var i = 0; i < 1000; i++)
            queue.Enqueue(new AuthTokenAudit { Username = "Unknown", Operation = "Flood" });

        await queue.StartAsync(TestContext.Current.CancellationToken);
        var written = await WaitForAudits("Flood", AuthFailureAuditQueue.BatchSize);
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        Assert.Equal(AuthFailureAuditQueue.BatchSize, written);
        Assert.Equal(AuthFailureAuditQueue.BatchSize, AuditCount("Flood"));
        await queue.StopAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2 * AuthFailureAuditQueue.BatchSize, AuditCount("Flood"));
    }
}
