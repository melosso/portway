using System.Data.Common;
using Npgsql;
using PortwayApi.Helpers;
using Xunit;

namespace PortwayApi.Tests.Helpers;

public class SqlErrorClassifierTests
{
    // Driver exceptions without public constructors are covered by the parity suite
    private sealed class FakeDbException(string message) : DbException(message);

    [Fact]
    public void PostgresRaiseException_IsIntentionalUserError()
    {
        var ex = new PostgresException("Order total may not be negative", "ERROR", "ERROR", "P0001");

        Assert.True(SqlErrorClassifier.IsIntentionalUserError(ex));
        Assert.Equal("Order total may not be negative", SqlErrorClassifier.GetUserMessage(ex));
    }

    [Fact]
    public void PostgresSystemError_IsNotIntentional()
    {
        var ex = new PostgresException("relation does not exist", "ERROR", "ERROR", "42P01");

        Assert.False(SqlErrorClassifier.IsIntentionalUserError(ex));
        Assert.Contains("42P01", SqlErrorClassifier.DescribeForLog(ex));
    }

    [Fact]
    public void UnknownDbException_IsNotTypeMismatch() =>
        Assert.False(SqlErrorClassifier.IsTypeMismatch(new FakeDbException("boom")));

    [Fact]
    public void UnknownDbException_IsNotIntentional_AndDescribesType()
    {
        var ex = new FakeDbException("boom");

        Assert.False(SqlErrorClassifier.IsIntentionalUserError(ex));
        Assert.Equal(nameof(FakeDbException), SqlErrorClassifier.DescribeForLog(ex));
        Assert.Equal("boom", SqlErrorClassifier.GetUserMessage(ex));
    }

    [Fact]
    public void StatementErrors_AreNotConnectionFailures()
    {
        Assert.False(SqlErrorClassifier.IsConnectionFailure(new FakeDbException("boom")));
        Assert.False(SqlErrorClassifier.IsConnectionFailure(new PostgresException("relation does not exist", "ERROR", "ERROR", "42P01")));
    }

    [Theory]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    [InlineData("MySql")]
    public async Task ClosedPort_IsConnectionFailure(string provider)
    {
        var port = ClosedPort();
        await using DbConnection connection = provider switch
        {
            "SqlServer" => new Microsoft.Data.SqlClient.SqlConnection($"Server=127.0.0.1,{port};Connect Timeout=3;Encrypt=false;Pooling=false"),
            "PostgreSql" => new NpgsqlConnection($"Host=127.0.0.1;Port={port};Timeout=3;Pooling=false"),
            _ => new MySqlConnector.MySqlConnection($"Server=127.0.0.1;Port={port};Connect Timeout=3;Pooling=false")
        };

        var ex = await Assert.ThrowsAnyAsync<DbException>(() => connection.OpenAsync(TestContext.Current.CancellationToken));

        Assert.True(SqlErrorClassifier.IsConnectionFailure(ex), ex.ToString());
    }

    private static int ClosedPort()
    {
        var listener = System.Net.Sockets.TcpListener.Create(0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
