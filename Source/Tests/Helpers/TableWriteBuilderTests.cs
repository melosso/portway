using Dapper;
using Microsoft.Data.Sqlite;
using PortwayApi.Classes;
using PortwayApi.Helpers;
using PortwayApi.Services.Providers;
using Xunit;

namespace PortwayApi.Tests.Helpers;

public class TableWriteBuilderTests
{
    private static EndpointDefinition ValidEndpoint() => new()
    {
        DatabaseObjectName = "Bins",
        DatabaseObjectType = "Table",
        WriteMode = "Table",
        PrimaryKey = "Id",
        AllowedColumns = ["Id", "Code", "Capacity"],
        Methods = ["GET", "POST", "PUT", "DELETE"]
    };

    [Fact]
    public void ValidConfig_Passes()
        => Assert.Null(TableWriteBuilder.ValidateConfig(ValidEndpoint()));

    [Theory]
    [InlineData("no-allowlist")]
    [InlineData("no-pk")]
    [InlineData("with-procedure")]
    [InlineData("bad-table-name")]
    [InlineData("tvf-type")]
    public void UnsafeConfig_IsRejected(string mutation)
    {
        var endpoint = ValidEndpoint();
        switch (mutation)
        {
            case "no-allowlist": endpoint.AllowedColumns = null; break;
            case "no-pk": endpoint.PrimaryKey = null; break;
            case "with-procedure": endpoint.Procedure = "dbo.ManageBins"; break;
            case "bad-table-name": endpoint.DatabaseObjectName = "Bins; DROP TABLE x"; break;
            case "tvf-type": endpoint.DatabaseObjectType = "TableValuedFunction"; break;
        }

        Assert.NotNull(TableWriteBuilder.ValidateConfig(endpoint));
    }

    [Fact]
    public void UnknownPayloadColumn_IsRejected_NotDropped()
    {
        var payload = new Dictionary<string, object?> { ["Code"] = "A1", ["IsAdmin"] = true };

        var ok = TableWriteBuilder.TryResolveColumns(ValidEndpoint(), payload, out _, out var error);

        Assert.False(ok);
        Assert.Contains("IsAdmin", error);
    }

    [Fact]
    public void CompiledSql_ContainsNoRawValues()
    {
        var provider = new SqliteProvider();
        var columns = new Dictionary<string, object?> { ["Code"] = "A'; DROP TABLE Bins;--", ["Capacity"] = 10 };

        var insert = TableWriteBuilder.BuildInsert(provider, "Bins", columns);

        Assert.DoesNotContain("DROP TABLE", insert.Sql);
        Assert.Contains("@", insert.Sql);
        Assert.Equal(2, insert.Parameters.Count);
    }

    [Fact]
    public async Task SqliteRoundtrip_InsertUpdateDelete()
    {
        var provider = new SqliteProvider();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await connection.ExecuteAsync("CREATE TABLE Bins (Id INTEGER PRIMARY KEY, Code TEXT NOT NULL, Capacity INTEGER)");

        var insert = TableWriteBuilder.BuildInsert(provider, "Bins",
            new Dictionary<string, object?> { ["Id"] = 1, ["Code"] = "A1", ["Capacity"] = 10 });
        await connection.ExecuteAsync(insert.Sql, insert.Parameters);

        var update = TableWriteBuilder.BuildUpdate(provider, "Bins", "Id", 1,
            new Dictionary<string, object?> { ["Capacity"] = 25 });
        Assert.Equal(1, await connection.ExecuteAsync(update.Sql, update.Parameters));

        var select = TableWriteBuilder.BuildSelectByKey(provider, "Bins", "Id", 1);
        var row = (await connection.QueryAsync(select.Sql, select.Parameters)).Single();
        Assert.Equal(25, Convert.ToInt32(row.Capacity));

        var delete = TableWriteBuilder.BuildDelete(provider, "Bins", "Id", 1);
        Assert.Equal(1, await connection.ExecuteAsync(delete.Sql, delete.Parameters));
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Bins"));
    }
}
