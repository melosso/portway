using Npgsql;
using PortwayApi.Helpers;
using PortwayApi.Services.Providers;
using Xunit;

namespace PortwayApi.Tests.Services;

public class SqlProviderFixesTests
{
    [Theory]
    [InlineData("Data Source=app.db3", SqlProviderType.Sqlite)]
    [InlineData("Data Source=app.db", SqlProviderType.Sqlite)]
    [InlineData("Data Source=:memory:", SqlProviderType.Sqlite)]
    [InlineData("Data Source=server", SqlProviderType.SqlServer)]
    public void Detector_ResolvesSqliteFileNames(string connectionString, SqlProviderType expected)
    {
        Assert.Equal(expected, SqlProviderDetector.Detect(connectionString));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(30)]
    public void PostgreSql_AcceptsEveryConnectionTimeout(int timeout)
    {
        var optimized = new PostgreSqlProvider().OptimizeConnectionString(
            "Host=localhost;Database=db;Username=u;Password=p", new SqlPoolingOptions(5, 100, timeout, true, "test"));

        using var source = new NpgsqlDataSourceBuilder(optimized).Build();
        var builder = new NpgsqlConnectionStringBuilder(optimized);
        Assert.True(builder.ConnectionPruningInterval <= builder.ConnectionIdleLifetime);
    }

    [Fact]
    public void TvfCall_UsesProviderQuoting()
    {
        var (sqlServer, _) = SqlTableValuedFunctionHelper.BuildFunctionCall(new MsSqlProvider(), "dbo", "Fn", [], []);
        var (postgres, _) = SqlTableValuedFunctionHelper.BuildFunctionCall(new PostgreSqlProvider(), "dbo", "Fn", [], []);

        Assert.Equal("SELECT * FROM [dbo].[Fn]()", sqlServer);
        Assert.Equal("""SELECT * FROM "public"."Fn"()""", postgres);
    }

    [Fact]
    public void TvfCall_OmitsTrailingDefault_OutsideSqlServer()
    {
        List<PortwayApi.Classes.TVFParameter> parameters = [new() { Name = "A", SqlType = "int", DefaultValue = "DEFAULT" }];

        Assert.EndsWith("[Fn](DEFAULT)", SqlTableValuedFunctionHelper.BuildFunctionCall(new MsSqlProvider(), "dbo", "Fn", [], parameters).FunctionCall);
        Assert.EndsWith("\"Fn\"()", SqlTableValuedFunctionHelper.BuildFunctionCall(new PostgreSqlProvider(), "dbo", "Fn", [], parameters).FunctionCall);
    }

    [Fact]
    public void Key_OnMySql_ComparesText_NumericKeyBindsLiteral()
    {
        var text = SqlTableWriteBuilder.BuildSelectByKey(new MySqlProvider(), "Items", "Id", "3abc");
        Assert.Contains("CAST(`Id` AS CHAR)", text.Sql);

        var numeric = SqlTableWriteBuilder.BuildDelete(new PostgreSqlProvider(), "Items", "Id", "1");
        Assert.Contains("'1'", numeric.Sql);
        Assert.Empty(numeric.Parameters);
    }
}

public class ODataColumnAllowlistTests
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Name"] = "ItemName",
        ["Price"] = "Price"
    };

    [Theory]
    [InlineData("Name eq 'Secret eq Hidden'")]
    [InlineData("contains(Name,'x') and Price gt 1.5")]
    [InlineData("name eq 'It''s' or not (Price le 2)")]
    [InlineData("Price eq null")]
    [InlineData("ItemName eq 'x'")]
    [InlineData("")]
    public void AllowedFilters_RefuseNothing(string expression)
    {
        Assert.Empty(ColumnMappingHelper.FindDisallowedFilterColumns(expression, Aliases));
    }

    [Theory]
    [InlineData("Salary gt 100", "Salary")]
    [InlineData("Name eq 'x' and Salary gt 100", "Salary")]
    [InlineData("startswith(Salary,'1')", "Salary")]
    [InlineData("Name ne \"'\" and startswith(Salary,\"a\") and Name ne \"'\"", "Salary")]
    [InlineData("Desc eq 'x'", "Desc")]
    [InlineData("desc eq 'x'", "desc")]
    [InlineData("Öffnung eq 1", "Öffnung")]
    [InlineData("Price mul Salary gt 1", "Salary")]
    [InlineData("not (Salary gt 1)", "Salary")]
    [InlineData("contains(tolower(Salary),'x')", "Salary")]
    [InlineData("Salary in ('a','b')", "Salary")]
    [InlineData("Category/Salary eq 1", "Category/Salary")]
    public void HiddenFilterColumns_AreRefused(string expression, string refused)
    {
        Assert.Equal([refused], ColumnMappingHelper.FindDisallowedFilterColumns(expression, Aliases));
    }

    [Theory]
    [InlineData("Name eq (")]
    [InlineData("Name eq")]
    [InlineData("'")]
    [InlineData("\"")]
    [InlineData("((((")]
    [InlineData("%")]
    [InlineData("Name eq 'a\0b' or")]
    [InlineData("#")]
    [InlineData("$it/Salary eq 1")]
    [InlineData("Lines/any(l: l/Salary eq 1)")]
    [InlineData("cast(Name,Edm.String) eq 'x'")]
    public void UnknownOrInvalidFilters_FailClosed(string expression)
    {
        Assert.NotEmpty(ColumnMappingHelper.FindDisallowedFilterColumns(expression, Aliases));
    }

    [Theory]
    [InlineData("Price desc, Name asc")]
    [InlineData("tolower(Name) DESC")]
    [InlineData("ItemName")]
    [InlineData("")]
    public void AllowedOrderBy_RefusesNothing(string expression)
    {
        Assert.Empty(ColumnMappingHelper.FindDisallowedOrderByColumns(expression, Aliases));
    }

    [Theory]
    [InlineData("Salary desc", "Salary")]
    [InlineData("Name desc, tolower(Salary) asc", "Salary")]
    [InlineData("Desc desc", "Desc")]
    [InlineData("Öffnung", "Öffnung")]
    public void HiddenOrderByColumns_AreRefused(string expression, string refused)
    {
        Assert.Equal([refused], ColumnMappingHelper.FindDisallowedOrderByColumns(expression, Aliases));
    }

    [Theory]
    [InlineData("Name,")]
    [InlineData("\"'\",Salary")]
    [InlineData("tolower(Name")]
    public void InvalidOrderBy_FailsClosed(string expression)
    {
        Assert.NotEmpty(ColumnMappingHelper.FindDisallowedOrderByColumns(expression, Aliases));
    }
}

public class RecordKeyParsingTests
{
    [Theory]
    [InlineData("Items(123)", "123")]
    [InlineData("Items('ABC')", "ABC")]
    [InlineData("my-items(guid'3f2a1b4c-1111-4222-8333-444455556666')", "3f2a1b4c-1111-4222-8333-444455556666")]
    [InlineData("my-items(3f2a1b4c-1111-4222-8333-444455556666)", "3f2a1b4c-1111-4222-8333-444455556666")]
    [InlineData("Items(ABC)", null)]
    [InlineData("Items", null)]
    public void KeyedName_ReadsEveryForm(string segment, string? expected)
    {
        Assert.Equal(expected, PortwayApi.Api.EndpointController.ReadKeyedName(segment));
    }

    [Theory]
    [InlineData("(123)", "123")]
    [InlineData("('ABC')", "ABC")]
    [InlineData("(guid'3f2a1b4c-1111-4222-8333-444455556666')", "3f2a1b4c-1111-4222-8333-444455556666")]
    [InlineData("(3f2a1b4c-1111-4222-8333-444455556666)", "3f2a1b4c-1111-4222-8333-444455556666")]
    [InlineData("guid'3f2a1b4c-1111-4222-8333-444455556666'", "3f2a1b4c-1111-4222-8333-444455556666")]
    [InlineData("'ABC'", "ABC")]
    [InlineData("3f2a1b4c-1111-4222-8333-444455556666", "3f2a1b4c-1111-4222-8333-444455556666")]
    [InlineData("123", "123")]
    [InlineData("details", null)]
    public void KeySegment_ReadsEveryForm(string segment, string? expected)
    {
        Assert.Equal(expected, PortwayApi.Api.EndpointController.ReadKeySegment(segment));
    }
}

public class CacheManagerCountingTests
{
    [Fact]
    public async Task UncountedRead_LeavesHitAndMissCountsAlone()
    {
        var options = new PortwayApi.Services.Caching.CacheOptions { Enabled = true };
        var metrics = new PortwayApi.Services.MetricsService();
        var cache = new PortwayApi.Services.Caching.CacheManager(
            new PortwayApi.Tests.Support.StaticOptionsMonitor<PortwayApi.Services.Caching.CacheOptions>(options),
            new PortwayApi.Services.Caching.MemoryCacheProvider(Microsoft.Extensions.Options.Options.Create(options)),
            metrics,
            new PortwayApi.Services.Telemetry.PortwayMetrics());

        await cache.SetAsync("present", "value", TimeSpan.FromMinutes(1));

        Assert.Equal("value", await cache.GetUncountedAsync<string>("present"));
        Assert.Null(await cache.GetUncountedAsync<string>("absent"));

        var snapshot = metrics.GetSnapshot("1h");
        Assert.Equal(0, snapshot.CacheHits);
        Assert.Equal(0, snapshot.CacheMisses);
    }
}
