using PortwayApi.Services;
using Xunit;

namespace PortwayApi.Tests.Services;

public class SqlCacheKeyTests
{
    private const string Query = "SELECT * FROM dbo.Accounts WHERE Name = @p0 AND City = @p1";

    [Fact]
    public void BuildCacheKey_ParameterValuesContainingSeparators_DoNotCollide()
    {
        var first = SqlRequestHandler.BuildCacheKey("prod", "Accounts", Query,
            new Dictionary<string, object> { ["p0"] = "x,p1=y", ["p1"] = "z" }, false);
        var second = SqlRequestHandler.BuildCacheKey("prod", "Accounts", Query,
            new Dictionary<string, object> { ["p0"] = "x", ["p1"] = "y,p1=z" }, false);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void BuildCacheKey_SameRequest_IsStable()
    {
        var parameters = new Dictionary<string, object> { ["p0"] = "x", ["p1"] = 1 };

        Assert.Equal(
            SqlRequestHandler.BuildCacheKey("prod", "Accounts", Query, parameters, true),
            SqlRequestHandler.BuildCacheKey("prod", "Accounts", Query, new Dictionary<string, object>(parameters), true));
    }

    [Fact]
    public void BuildCacheKey_DiffersByQueryAndCount()
    {
        var parameters = new Dictionary<string, object> { ["p0"] = "x" };
        var key = SqlRequestHandler.BuildCacheKey("prod", "Accounts", Query, parameters, false);

        Assert.NotEqual(key, SqlRequestHandler.BuildCacheKey("prod", "Accounts", Query + " ORDER BY Name", parameters, false));
        Assert.NotEqual(key, SqlRequestHandler.BuildCacheKey("prod", "Accounts", Query, parameters, true));
        Assert.NotEqual(key, SqlRequestHandler.BuildCacheKey("prod", "Accounts", Query, new Dictionary<string, object> { ["p0"] = 1 }, false));
    }
}
