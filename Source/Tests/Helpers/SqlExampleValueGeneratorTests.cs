using PortwayApi.Classes;
using PortwayApi.Classes.OpenApi;
using Xunit;

namespace PortwayApi.Tests.Helpers;

public class SqlExampleValueGeneratorTests
{
    // FromColumn

    [Fact]
    public void FromColumn_Nullable_ReturnsNull()
    {
        var col = new ColumnMetadata { ClrType = "System.String", IsNullable = true };
        Assert.Null(SqlExampleValueGenerator.FromColumn(col));
    }

    [Theory]
    [InlineData("System.String",  false, false, "example")]
    [InlineData("System.String",  false, true,  "ABC123")]   // primary key string
    [InlineData("System.Boolean", false, false, "true")]
    public void FromColumn_NonNullable_ReturnsValue(string clrType, bool isNullable, bool isPk, string expectedContains)
    {
        var col = new ColumnMetadata { ClrType = clrType, IsNullable = isNullable, IsPrimaryKey = isPk };
        var result = SqlExampleValueGenerator.FromColumn(col);
        Assert.NotNull(result);
        Assert.Contains(expectedContains, result.ToJsonString());
    }

    [Fact]
    public void FromColumn_Int32PrimaryKey_Returns1()
    {
        var col = new ColumnMetadata { ClrType = "System.Int32", IsNullable = false, IsPrimaryKey = true };
        var result = SqlExampleValueGenerator.FromColumn(col);
        Assert.NotNull(result);
        Assert.Equal("1", result.ToJsonString());
    }

    [Fact]
    public void FromColumn_Int32NonPk_Returns42()
    {
        var col = new ColumnMetadata { ClrType = "System.Int32", IsNullable = false, IsPrimaryKey = false };
        var result = SqlExampleValueGenerator.FromColumn(col);
        Assert.NotNull(result);
        Assert.Equal("42", result.ToJsonString());
    }

    [Fact]
    public void FromColumn_Guid_ReturnsValidGuid()
    {
        var col = new ColumnMetadata { ClrType = "System.Guid", IsNullable = false };
        var result = SqlExampleValueGenerator.FromColumn(col);
        Assert.NotNull(result);
        Assert.True(Guid.TryParse(result.GetValue<string>(), out _));
    }

    [Fact]
    public void FromColumn_UnknownType_ReturnsValue()
    {
        var col = new ColumnMetadata { ClrType = "Some.Unknown.Type", IsNullable = false };
        var result = SqlExampleValueGenerator.FromColumn(col);
        Assert.NotNull(result);
        Assert.Equal("\"value\"", result.ToJsonString());
    }

    // FromParameter

    [Fact]
    public void FromParameter_EmptyPropertyName_ReturnsNull()
    {
        var p = new ParameterMetadata { ClrType = "System.String" };
        Assert.Null(SqlExampleValueGenerator.FromParameter(p, ""));
    }

    [Fact]
    public void FromParameter_NameContainsId_IntType_Returns1()
    {
        var p = new ParameterMetadata { ClrType = "System.Int32" };
        var result = SqlExampleValueGenerator.FromParameter(p, "UserId");
        Assert.NotNull(result);
        Assert.Equal("1", result.ToJsonString());
    }

    [Fact]
    public void FromParameter_NameContainsId_GuidType_ReturnsGuid()
    {
        var p = new ParameterMetadata { ClrType = "System.Guid" };
        var result = SqlExampleValueGenerator.FromParameter(p, "CustomerId");
        Assert.NotNull(result);
        Assert.True(Guid.TryParse(result.GetValue<string>(), out _));
    }

    [Fact]
    public void FromParameter_NameContainsName_ReturnsExampleName()
    {
        var p = new ParameterMetadata { ClrType = "System.String" };
        var result = SqlExampleValueGenerator.FromParameter(p, "FirstName");
        Assert.NotNull(result);
        Assert.Contains("Example", result.GetValue<string>());
    }

    [Fact]
    public void FromParameter_NameContainsEmail_ReturnsEmail()
    {
        var p = new ParameterMetadata { ClrType = "System.String" };
        var result = SqlExampleValueGenerator.FromParameter(p, "EmailAddress");
        Assert.NotNull(result);
        Assert.Equal("user@example.com", result.GetValue<string>());
    }

    [Fact]
    public void FromParameter_NameStartsWithIs_ReturnsTrue()
    {
        var p = new ParameterMetadata { ClrType = "System.Boolean" };
        var result = SqlExampleValueGenerator.FromParameter(p, "IsActive");
        Assert.NotNull(result);
        Assert.Equal("true", result.ToJsonString());
    }

    [Fact]
    public void FromParameter_NameContainsPrice_ReturnsDecimal()
    {
        var p = new ParameterMetadata { ClrType = "System.Decimal" };
        var result = SqlExampleValueGenerator.FromParameter(p, "UnitPrice");
        Assert.NotNull(result);
        Assert.Contains("99", result.ToJsonString());
    }

    [Fact]
    public void FromParameter_FallbackString_ReturnsExamplePropertyName()
    {
        var p = new ParameterMetadata { ClrType = "System.String" };
        var result = SqlExampleValueGenerator.FromParameter(p, "Description");
        Assert.NotNull(result);
        Assert.Contains("description", result.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromParameter_NullableUnknownType_ReturnsNull()
    {
        var p = new ParameterMetadata { ClrType = "Some.Unknown", IsNullable = true };
        var result = SqlExampleValueGenerator.FromParameter(p, "SomeField");
        Assert.Null(result);
    }

    [Fact]
    public void FromParameter_NonNullableUnknownType_ReturnsValue()
    {
        var p = new ParameterMetadata { ClrType = "Some.Unknown", IsNullable = false };
        var result = SqlExampleValueGenerator.FromParameter(p, "SomeField");
        Assert.NotNull(result);
        Assert.Equal("\"value\"", result.ToJsonString());
    }
}
