namespace PortwayApi.Tests.Parity;

using Dapper;
using PortwayApi.Classes;
using PortwayApi.Helpers;
using SqlKata;
using Xunit;

/// <summary>Parity oracle: the same scenarios run against every provider's real database (issue #29)</summary>
[Trait("Category", "Parity")]
public abstract class ProviderParityTests<TFixture>(TFixture fixture) : IClassFixture<TFixture>
    where TFixture : ParityDatabaseFixture
{
    private readonly TFixture _fixture = fixture;

    private ODataToSqlConverter CreateConverter() => new(
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
    public async Task TableWrite_InsertUpdateDelete_Roundtrip()
    {
        var provider = _fixture.Provider;
        await using var connection = provider.CreateConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        try
        {
            var insert = PortwayApi.Helpers.SqlTableWriteBuilder.BuildInsert(provider, _fixture.QualifiedProductsTable,
                new Dictionary<string, object?> { ["Id"] = 80, ["Name"] = "Detonator", ["Price"] = 3.50m });
            await connection.ExecuteAsync(insert.Sql, insert.Parameters);

            var update = PortwayApi.Helpers.SqlTableWriteBuilder.BuildUpdate(provider, _fixture.QualifiedProductsTable, "Id", 80,
                new Dictionary<string, object?> { ["Price"] = 4.25m });
            Assert.Equal(1, await connection.ExecuteAsync(update.Sql, update.Parameters));

            var select = PortwayApi.Helpers.SqlTableWriteBuilder.BuildSelectByKey(provider, _fixture.QualifiedProductsTable, "Id", 80);
            var row = (await connection.QueryAsync(select.Sql, select.Parameters)).Single();
            Assert.Equal(4.25m, Convert.ToDecimal(row.Price));
        }
        finally
        {
            var delete = PortwayApi.Helpers.SqlTableWriteBuilder.BuildDelete(provider, _fixture.QualifiedProductsTable, "Id", 80);
            await connection.ExecuteAsync(delete.Sql, delete.Parameters);
        }
    }

    private RelationalExpandSpec CategorySpec() => new(
        "Category", _fixture.QualifiedCategoriesTable, "CategoryId", "CategoryId",
        new[] { "CategoryId", "CategoryName" });

    [DockerFact]
    public async Task Expand_InnerJoin_ReturnsMatchedRowsWithTargetColumns()
    {
        // Products 1, 2 and 4 have a category; 3 and 5 do not, so the inner join drops them
        var query = new Query(_fixture.QualifiedProductsTable)
            .Select($"{_fixture.QualifiedProductsTable}.Id", $"{_fixture.QualifiedProductsTable}.Name");
        query = OdataExpandJoinBuilder.Apply(query, _fixture.QualifiedProductsTable, new[] { CategorySpec() });
        var compiled = _fixture.Provider.GetCompiler().Compile(query);

        await using var connection = _fixture.Provider.CreateConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        var rows = (await connection.QueryAsync(compiled.Sql, new DynamicParameters(compiled.NamedBindings)))
            .Cast<IDictionary<string, object>>()
            .ToList();

        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.True(r.ContainsKey("Category.CategoryName")));
    }

    [DockerFact]
    public async Task Expand_NestedShape_AliasesTargetColumns()
    {
        var query = new Query(_fixture.QualifiedProductsTable)
            .Select($"{_fixture.QualifiedProductsTable}.Id", $"{_fixture.QualifiedProductsTable}.Name");
        query = OdataExpandJoinBuilder.Apply(query, _fixture.QualifiedProductsTable, new[] { CategorySpec() });
        var compiled = _fixture.Provider.GetCompiler().Compile(query);

        await using var connection = _fixture.Provider.CreateConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        var rows = (await connection.QueryAsync(compiled.Sql, new DynamicParameters(compiled.NamedBindings))).ToList();

        var navMap = new Dictionary<string, string> { ["CategoryName"] = "Name" };
        var nested = OdataExpandResponseShaper.Nest(rows, new[] { ("Category", (IReadOnlyDictionary<string, string>)navMap) });

        // Anvil (Id 1) maps to category 10 (Tools)
        var anvil = nested.Single(r => Convert.ToInt32(r["Id"]) == 1);
        var category = Assert.IsType<Dictionary<string, object>>(anvil["Category"]);
        Assert.Equal("Tools", (string)category["Name"]);
        Assert.False(anvil.ContainsKey("Category.CategoryName"));
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
