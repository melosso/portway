using PortwayApi.Classes;
using PortwayApi.Helpers;
using SqlKata;
using SqlKata.Compilers;
using Xunit;

namespace PortwayApi.Tests.Helpers;

public class OdataExpandJoinBuilderTests
{
    private static RelationalExpandSpec Category() =>
        new("Category", "dbo.Assortments", "Assortment", "AssortmentID", new[] { "AssortmentID", "Name" });

    private static string Compile(Compiler compiler, string rootTable, params RelationalExpandSpec[] specs)
    {
        var query = new Query(rootTable).Select($"{rootTable}.ItemCode");
        query = OdataExpandJoinBuilder.Apply(query, rootTable, specs.ToList());
        return compiler.Compile(query).Sql;
    }

    [Fact]
    public void SqlServer_EmitsInnerJoinOnKeys()
    {
        var sql = Compile(new SqlServerCompiler(), "dbo.Items", Category());

        Assert.Contains("INNER JOIN [dbo].[Assortments] AS [Category]", sql);
        Assert.Contains("[Category].[AssortmentID] = [dbo].[Items].[Assortment]", sql);
    }

    [Fact]
    public void SqlServer_AliasesTargetColumnsAsDottedKeys()
    {
        var sql = Compile(new SqlServerCompiler(), "dbo.Items", Category());

        Assert.Contains("[Category].[Name] AS [Category.Name]", sql);
        Assert.Contains("[Category].[AssortmentID] AS [Category.AssortmentID]", sql);
    }

    [Fact]
    public void Postgres_QuotesWithDoubleQuotes()
    {
        var sql = Compile(new PostgresCompiler(), "public.Items",
            new RelationalExpandSpec("Category", "public.Assortments", "Assortment", "AssortmentID", new[] { "Name" }));

        Assert.Contains("\"public\".\"Assortments\" AS \"Category\"", sql);
        Assert.Contains("\"Category\".\"AssortmentID\" = \"public\".\"Items\".\"Assortment\"", sql);
    }

    [Fact]
    public void MySql_QuotesWithBackticks()
    {
        var sql = Compile(new MySqlCompiler(), "Items",
            new RelationalExpandSpec("Category", "Assortments", "Assortment", "AssortmentID", new[] { "Name" }));

        Assert.Contains("`Assortments` AS `Category`", sql);
    }

    [Fact]
    public void MultipleNavigations_EmitOneJoinEach()
    {
        var sql = Compile(new SqlServerCompiler(), "dbo.Items",
            Category(),
            new RelationalExpandSpec("Supplier", "dbo.Suppliers", "SupplierId", "Id", new[] { "Id", "Name" }));

        Assert.Contains("AS [Category]", sql);
        Assert.Contains("AS [Supplier]", sql);
        Assert.Contains("[Supplier].[Id] = [dbo].[Items].[SupplierId]", sql);
    }

    [Fact]
    public void NoSpecs_LeavesQueryUnchanged()
    {
        var sql = Compile(new SqlServerCompiler(), "dbo.Items");

        Assert.DoesNotContain("JOIN", sql, System.StringComparison.OrdinalIgnoreCase);
    }
}
