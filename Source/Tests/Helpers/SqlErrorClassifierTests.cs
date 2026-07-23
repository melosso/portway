using System.Data.Common;
using Npgsql;
using PortwayApi.Helpers;
using Xunit;

namespace PortwayApi.Tests.Helpers;

public class SqlErrorClassifierTests
{
    // SqlException and MySqlException have internal constructors, so unit coverage
    // uses PostgresException plus a plain DbException; the driver-specific arms are
    // exercised by the Testcontainers parity suite
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
    public void UnknownDbException_IsNotIntentional_AndDescribesType()
    {
        var ex = new FakeDbException("boom");

        Assert.False(SqlErrorClassifier.IsIntentionalUserError(ex));
        Assert.Equal(nameof(FakeDbException), SqlErrorClassifier.DescribeForLog(ex));
        Assert.Equal("boom", SqlErrorClassifier.GetUserMessage(ex));
    }
}
