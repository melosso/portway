namespace PortwayApi.Tests.Parity;

using Xunit;

public sealed class PostgreSqlProviderParityTests(PostgreSqlParityFixture fixture)
    : ProviderParityTests<PostgreSqlParityFixture>(fixture)
{
    private readonly PostgreSqlParityFixture _pgFixture = fixture;

    // The shared scenario covers RETURNS SETOF composite; this covers the RETURNS TABLE argument shape
    [DockerFact]
    public async Task TvfColumns_ForReturnsTableFunction_AreDiscovered()
    {
        await using var connection = _pgFixture.Provider.CreateConnection(_pgFixture.ConnectionString);
        await connection.OpenAsync();

        var columns = await _pgFixture.Provider.GetTvfColumnsAsync(
            connection, "public", "products_summary", CancellationToken.None);

        Assert.Equal(2, columns.Count);
        Assert.Equal("Name", columns[0].DatabaseColumnName);
        Assert.Equal("System.String", columns[0].ClrType);
        Assert.Equal("Price", columns[1].DatabaseColumnName);
        Assert.Equal("System.Decimal", columns[1].ClrType);
    }
}
