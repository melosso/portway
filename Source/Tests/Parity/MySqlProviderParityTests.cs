namespace PortwayApi.Tests.Parity;

using System.Net;
using Dapper;
using MySqlConnector;
using PortwayApi.Services.Providers;
using Xunit;

public sealed class MySqlProviderParityTests(MySqlParityFixture fixture)
    : ProviderParityTests<MySqlParityFixture>(fixture)
{
    [DockerFact]
    public async Task MissingProcedureArguments_AreAddedWithoutRepeatingTheMetadataQuery()
    {
        var provider = new MySqlProvider();
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new MySqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync(ct);

        async Task<IEnumerable<string>> AddMissing()
        {
            var parameters = new DynamicParameters();
            parameters.Add("@Method", "GET");
            await provider.AddMissingProcedureArgumentsAsync(connection, Fixture.ProcedureSchema, Fixture.GuidKeyProcedureName, parameters, ct);
            return parameters.ParameterNames;
        }

        async Task<long> StatementCount() =>
            long.Parse(await connection.QuerySingleAsync<string>("SELECT VARIABLE_VALUE FROM performance_schema.session_status WHERE VARIABLE_NAME = 'Questions'"));

        Assert.Equal(["Id", "Method", "Name"], (await AddMissing()).Order());

        var before = await StatementCount();
        await AddMissing();
        var after = await StatementCount();

        Assert.Equal(1, after - before);
    }

    [Theory]
    [InlineData("Server=localhost;Database=db;", MySqlConnector.MySqlGuidFormat.None)]
    [InlineData("Server=localhost;Database=db;GuidFormat=Char36;", MySqlConnector.MySqlGuidFormat.Char36)]
    [InlineData("Server=localhost;Database=db;OldGuids=true;", MySqlConnector.MySqlGuidFormat.Default)]
    public void ConnectionString_MapsChar36ToStringUnlessConfigured(string connectionString, MySqlConnector.MySqlGuidFormat expected)
    {
        var optimized = new MySqlProvider().OptimizeConnectionString(connectionString, new SqlPoolingOptions(5, 100, 15, true, "PortwayTest"));

        Assert.Equal(expected, new MySqlConnector.MySqlConnectionStringBuilder(optimized).GuidFormat);
    }
}

public sealed class MySqlProviderHttpParityTests(MySqlParityFixture fixture)
    : ProviderHttpParityTests<MySqlParityFixture>(fixture)
{
    // char(36) accepts any short text
    protected override HttpStatusCode NonGuidKeyOnGuidColumnStatus => HttpStatusCode.NotFound;

    [DockerFact]
    public async Task NonGuidChar36Value_StaysReadable()
    {
        Assert.Equal(HttpStatusCode.Created, (await Send(HttpMethod.Post, Url("Native"), """{ "Id": "plain", "Name": "Text" }""")).Status);

        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, Url("Native", "('plain')"))).Status);
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, Url("Native"))).Status);
    }
}
