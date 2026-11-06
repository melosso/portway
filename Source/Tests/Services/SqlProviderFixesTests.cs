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
    [InlineData("Price desc, Name asc")]
    [InlineData("Price eq null")]
    [InlineData("ItemName eq 'x'")]
    [InlineData("Name eq guid'3f2a1b4c-1111-4222-8333-444455556666'")]
    [InlineData("")]
    public void AllowedExpressions_RefuseNothing(string expression)
    {
        Assert.Empty(ColumnMappingHelper.FindDisallowedColumns(expression, Aliases));
    }

    [Theory]
    [InlineData("Salary gt 100", "Salary")]
    [InlineData("Name eq 'x' and Salary gt 100", "Salary")]
    [InlineData("startswith(Salary,'1')", "Salary")]
    [InlineData("Salary desc", "Salary")]
    public void HiddenColumns_AreRefused(string expression, string refused)
    {
        Assert.Equal([refused], ColumnMappingHelper.FindDisallowedColumns(expression, Aliases));
    }
}
