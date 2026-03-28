using System.Text.Json;
using PortwayApi.Helpers;
using Xunit;

namespace PortwayApi.Tests.Helpers;

public class JsonValueComparerTests
{
    private static JsonElement Field(string json)
        => JsonDocument.Parse(json).RootElement;

    // String comparisons
    [Theory]
    [InlineData("\"apple\"", "apple", "eq", true)]
    [InlineData("\"apple\"", "APPLE", "eq", true)]   // case-insensitive
    [InlineData("\"apple\"", "banana", "eq", false)]
    [InlineData("\"apple\"", "apple", "ne", false)]
    [InlineData("\"apple\"", "banana", "ne", true)]
    [InlineData("\"b\"",     "a",      "gt", true)]
    [InlineData("\"a\"",     "b",      "lt", true)]
    [InlineData("\"a\"",     "a",      "ge", true)]
    [InlineData("\"a\"",     "a",      "le", true)]
    public void Compare_String_ReturnsExpected(string fieldJson, string target, string op, bool expected)
    {
        var result = JsonValueComparer.Compare(Field(fieldJson), target, op);
        Assert.Equal(expected, result);
    }

    // Numeric comparisons
    [Theory]
    [InlineData("42",   "42",  "eq", true)]
    [InlineData("42",   "43",  "eq", false)]
    [InlineData("42",   "42",  "ne", false)]
    [InlineData("43",   "42",  "gt", true)]
    [InlineData("41",   "42",  "lt", true)]
    [InlineData("42",   "42",  "ge", true)]
    [InlineData("42",   "42",  "le", true)]
    [InlineData("3.14", "3.14","eq", true)]
    public void Compare_Number_ReturnsExpected(string fieldJson, string target, string op, bool expected)
    {
        var result = JsonValueComparer.Compare(Field(fieldJson), target, op);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Compare_NumberNonParsableTarget_ReturnsFalse()
    {
        var result = JsonValueComparer.Compare(Field("42"), "not-a-number", "eq");
        Assert.False(result);
    }

    // Boolean comparisons
    [Theory]
    [InlineData("true",  "true",  "eq", true)]
    [InlineData("true",  "false", "eq", false)]
    [InlineData("false", "true",  "ne", true)]
    [InlineData("true",  "true",  "ne", false)]
    public void Compare_Boolean_ReturnsExpected(string fieldJson, string target, string op, bool expected)
    {
        var result = JsonValueComparer.Compare(Field(fieldJson), target, op);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Compare_BooleanUnsupportedOp_ReturnsFalse()
    {
        var result = JsonValueComparer.Compare(Field("true"), "true", "gt");
        Assert.False(result);
    }

    // Unknown / null
    [Fact]
    public void Compare_NullValue_ReturnsFalse()
    {
        var result = JsonValueComparer.Compare(Field("null"), "null", "eq");
        Assert.False(result);
    }

    [Fact]
    public void Compare_UnknownOperation_ReturnsFalse()
    {
        var result = JsonValueComparer.Compare(Field("\"hello\""), "hello", "contains");
        Assert.False(result);
    }
}
