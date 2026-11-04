namespace PortwayApi.Tests.Parity;

using System.Net;
using PortwayApi.Services.Providers;
using Xunit;

public sealed class MySqlProviderParityTests(MySqlParityFixture fixture)
    : ProviderParityTests<MySqlParityFixture>(fixture)
{
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
