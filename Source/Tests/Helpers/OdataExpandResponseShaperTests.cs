using PortwayApi.Helpers;
using Xunit;

namespace PortwayApi.Tests.Helpers;

public class OdataExpandResponseShaperTests
{
    private static (string, IReadOnlyDictionary<string, string>) Nav(
        string name, params (string Db, string Alias)[] map) =>
        (name, map.ToDictionary(m => m.Db, m => m.Alias));

    private static Dictionary<string, object> Row(params (string Key, object Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value);

    [Fact]
    public void NestsDottedKeysUnderNavObject()
    {
        var rows = new object[]
        {
            Row(("ProductNumber", "A-100"), ("Category.AssortmentID", 10), ("Category.Name", "Tools"))
        };

        var result = OdataExpandResponseShaper.Nest(rows, new[] { Nav("Category") });

        var nested = Assert.IsType<Dictionary<string, object>>(result[0]["Category"]);
        Assert.Equal(10, nested["AssortmentID"]);
        Assert.Equal("Tools", nested["Name"]);
        Assert.Equal("A-100", result[0]["ProductNumber"]);
        Assert.False(result[0].ContainsKey("Category.Name"));
    }

    [Fact]
    public void AppliesTargetAliases()
    {
        var rows = new object[]
        {
            Row(("Category.AssortmentID", 10), ("Category.Name", "Tools"))
        };

        var result = OdataExpandResponseShaper.Nest(rows,
            new[] { Nav("Category", ("AssortmentID", "CategoryId"), ("Name", "CategoryName")) });

        var nested = (Dictionary<string, object>)result[0]["Category"];
        Assert.Equal(10, nested["CategoryId"]);
        Assert.Equal("Tools", nested["CategoryName"]);
    }

    [Fact]
    public void UnmappedNestedColumnKeepsDatabaseName()
    {
        var rows = new object[] { Row(("Category.Extra", 1)) };

        var result = OdataExpandResponseShaper.Nest(rows, new[] { Nav("Category", ("Name", "CategoryName")) });

        var nested = (Dictionary<string, object>)result[0]["Category"];
        Assert.Equal(1, nested["Extra"]);
    }

    [Fact]
    public void MultipleNavigations_NestIndependently()
    {
        var rows = new object[]
        {
            Row(("Id", 1), ("Category.Name", "Tools"), ("Supplier.Name", "Acme"))
        };

        var result = OdataExpandResponseShaper.Nest(rows, new[] { Nav("Category"), Nav("Supplier") });

        Assert.Equal("Tools", ((Dictionary<string, object>)result[0]["Category"])["Name"]);
        Assert.Equal("Acme", ((Dictionary<string, object>)result[0]["Supplier"])["Name"]);
        Assert.Equal(1, result[0]["Id"]);
    }

    [Fact]
    public void NavWithNoColumns_StillEmitsEmptyObject()
    {
        var rows = new object[] { Row(("Id", 1)) };

        var result = OdataExpandResponseShaper.Nest(rows, new[] { Nav("Category") });

        var nested = Assert.IsType<Dictionary<string, object>>(result[0]["Category"]);
        Assert.Empty(nested);
    }
}
