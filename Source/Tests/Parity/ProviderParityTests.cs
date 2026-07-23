namespace PortwayApi.Tests.Parity;

using Dapper;
using PortwayApi.Classes;
using Xunit;

/// <summary>Parity oracle: the same scenarios run against every provider's real database (issue #29)</summary>
[Trait("Category", "Parity")]
public abstract class ProviderParityTests<TFixture>(TFixture fixture) : IClassFixture<TFixture>
    where TFixture : ParityDatabaseFixture
{
    private readonly TFixture _fixture = fixture;

    private ODataToSqlConverter CreateConverter() => new(
        new EdmModelBuilder(),
        [new PortwayApi.Services.Providers.MsSqlProvider(), new PortwayApi.Services.Providers.PostgreSqlProvider(),
         new PortwayApi.Services.Providers.MySqlProvider(), new PortwayApi.Services.Providers.SqliteProvider()]);

    [DockerFact]
    public async Task HealthQuery_Executes()
    {
        await using var connection = _fixture.Provider.CreateConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        var result = await connection.ExecuteScalarAsync<int>(_fixture.Provider.HealthCheckQuery);

        Assert.Equal(1, result);
    }

    [DockerFact]
    public async Task OData_FilterOrderAndPaging_ReturnsExpectedRows()
    {
        var odataParams = new Dictionary<string, string>
        {
            ["filter"] = "Price gt 20",
            ["orderby"] = "Price desc",
            ["top"] = "2",
            ["skip"] = "1",
        };

        var (sql, parameters) = CreateConverter().ConvertToSQL(
            _fixture.QualifiedProductsTable, odataParams, _fixture.ProviderType);

        await using var connection = _fixture.Provider.CreateConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        var rows = (await connection.QueryAsync(sql, new DynamicParameters(parameters))).ToList();

        // Price > 20 leaves 149.50, 79.99, 33.10, 25.00; skip 1 take 2 = 79.99 and 33.10
        Assert.Equal(2, rows.Count);
        Assert.Equal("Rocket Skates", (string)rows[0].Name);
        Assert.Equal("Tornado Kit", (string)rows[1].Name);
    }

    [DockerFact]
    public async Task OData_ContainsFilter_MatchesRows()
    {
        var odataParams = new Dictionary<string, string> { ["filter"] = "contains(Name,'Kit')" };

        var (sql, parameters) = CreateConverter().ConvertToSQL(
            _fixture.QualifiedProductsTable, odataParams, _fixture.ProviderType);

        await using var connection = _fixture.Provider.CreateConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        var rows = (await connection.QueryAsync(sql, new DynamicParameters(parameters))).ToList();

        Assert.Single(rows);
        Assert.Equal("Tornado Kit", (string)rows[0].Name);
    }

    [DockerFact]
    public async Task OData_CountSql_MatchesFilter()
    {
        var odataParams = new Dictionary<string, string> { ["filter"] = "Price lt 50" };

        var (sql, parameters) = CreateConverter().ConvertToCountSQL(
            _fixture.QualifiedProductsTable, odataParams, _fixture.ProviderType);

        await using var connection = _fixture.Provider.CreateConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        var count = await connection.ExecuteScalarAsync<long>(sql, new DynamicParameters(parameters));

        // 25.00, 5.25 and 33.10 fall under 50
        Assert.Equal(3, count);
    }

    [DockerFact]
    public async Task TvfColumns_AreDiscovered_OrEmptyWhenUnsupported()
    {
        await using var connection = _fixture.Provider.CreateConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        if (!_fixture.Provider.SupportsTvf)
        {
            var none = await _fixture.Provider.GetTvfColumnsAsync(connection, _fixture.ProcedureSchema, "does_not_matter", CancellationToken.None);
            Assert.Empty(none);
            return;
        }

        var columns = await _fixture.Provider.GetTvfColumnsAsync(
            connection, _fixture.ProcedureSchema, _fixture.TvfName, CancellationToken.None);

        Assert.Equal(_fixture.TvfColumnCount, columns.Count);
        var price = columns.Single(c => c.DatabaseColumnName.Equals("Price", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("System.Decimal", price.ClrType);
    }

    [DockerFact]
    public async Task OData_TemplateDboSchema_ResolvesPerProvider()
    {
        // Copied entity.json templates say dbo; the resolver maps that to each provider's own schema
        var (sql, parameters) = CreateConverter().ConvertToSQL(
            "dbo.Products", new Dictionary<string, string>(), _fixture.ProviderType);

        await using var connection = _fixture.Provider.CreateConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        var rows = (await connection.QueryAsync(sql, new DynamicParameters(parameters))).ToList();

        Assert.Equal(5, rows.Count);
    }

    [DockerFact]
    public async Task OData_YearFunction_FiltersByDatePart()
    {
        var odataParams = new Dictionary<string, string> { ["filter"] = "year(ReleasedAt) eq 2024" };

        var (sql, parameters) = CreateConverter().ConvertToSQL(
            _fixture.QualifiedProductsTable, odataParams, _fixture.ProviderType);

        await using var connection = _fixture.Provider.CreateConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        var rows = (await connection.QueryAsync(sql, new DynamicParameters(parameters))).ToList();

        Assert.Equal(3, rows.Count);
    }

    [DockerFact]
    public async Task OData_StartsWithFilter_MatchesRow()
    {
        var odataParams = new Dictionary<string, string> { ["filter"] = "startswith(Name,'Bird')" };

        var (sql, parameters) = CreateConverter().ConvertToSQL(
            _fixture.QualifiedProductsTable, odataParams, _fixture.ProviderType);

        await using var connection = _fixture.Provider.CreateConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        var rows = (await connection.QueryAsync(sql, new DynamicParameters(parameters))).ToList();

        Assert.Single(rows);
        Assert.Equal("Bird Seed", (string)rows[0].Name);
    }

    [DockerFact]
    public async Task WriteProcedure_InsertsAndReturnsRow_WithDialectInvocation()
    {
        // Mirrors SqlRequestHandler's write path: DynamicParameters plus BuildProcedureInvocation
        var parameters = new DynamicParameters();
        parameters.Add("@Method", "INSERT");
        parameters.Add("@Id", 60);
        parameters.Add("@Name", "Dynamite");
        parameters.Add("@Price", 9.75m);

        var (commandText, commandType) = _fixture.Provider.BuildProcedureInvocation(
            _fixture.ProcedureSchema, _fixture.WriteProcedureName, parameters.ParameterNames.ToList());

        await using var connection = _fixture.Provider.CreateConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        try
        {
            var rows = (await connection.QueryAsync(commandText, parameters, commandType: commandType)).ToList();

            var row = Assert.Single(rows);
            Assert.Equal("Dynamite", (string)row.Name);
            Assert.Equal(60, Convert.ToInt32(row.Id));
        }
        finally
        {
            // Leave the seed data untouched for the read scenarios
            await connection.ExecuteAsync(_fixture.DeleteProductByIdSql, new { Id = 60 });
        }
    }

    [DockerFact]
    public async Task ProcedureParameters_AreDiscovered()
    {
        await using var connection = _fixture.Provider.CreateConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        var parameters = await _fixture.Provider.GetProcedureParametersAsync(
            connection, _fixture.ProcedureSchema, _fixture.ProcedureName, CancellationToken.None);

        var parameter = Assert.Single(parameters);
        Assert.Equal("System.Decimal", parameter.ClrType);
    }
}
