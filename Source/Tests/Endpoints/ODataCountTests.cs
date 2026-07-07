using PortwayApi.Classes;
using PortwayApi.Classes.Providers;
using PortwayApi.Services.Providers;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

/// <summary>Verifies the $count=true conversion produces a COUNT query shaped only by the filter</summary>
public class ODataCountTests
{
    private static ODataToSqlConverter CreateConverter()
        => new(new EdmModelBuilder(), new[] { new MsSqlProvider() });

    [Fact]
    public void CountQuery_Contains_Count_And_Filter()
    {
        var converter = CreateConverter();
        var odataParams = new Dictionary<string, string>
        {
            ["top"] = "11",
            ["skip"] = "0",
            ["select"] = "ItemCode,Description",
            ["orderby"] = "ItemCode",
            ["filter"] = "Price gt 5"
        };

        var (sql, parameters) = converter.ConvertToCountSQL("dbo.Items", odataParams, SqlProviderType.SqlServer);

        Assert.Contains("COUNT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OFFSET", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(parameters);
    }

    [Fact]
    public void CountQuery_WithoutFilter_HasNoWhere()
    {
        var converter = CreateConverter();
        var (sql, _) = converter.ConvertToCountSQL("dbo.Items", new Dictionary<string, string>(), SqlProviderType.SqlServer);

        Assert.Contains("COUNT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WHERE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RowQuery_IsUnchanged_ByCountSupport()
    {
        var converter = CreateConverter();
        var odataParams = new Dictionary<string, string> { ["top"] = "11", ["skip"] = "0" };

        var (sql, _) = converter.ConvertToSQL("dbo.Items", odataParams, SqlProviderType.SqlServer);

        Assert.DoesNotContain("COUNT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SELECT", sql, StringComparison.OrdinalIgnoreCase);
    }
}
