using System.Data.Common;
using PortwayApi.Helpers;
using Xunit;

namespace PortwayApi.Tests.Helpers;

public sealed class SecretReferenceTests
{
    private static readonly Dictionary<string, string> Values = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DB-PASSWORD"] = "p@ss",
        ["ERP-KEY"] = "k1",
        ["NESTED"] = "${secret:ERP-KEY}",
        ["SEMI"] = "x;Database=evil",
        ["CRLF"] = "a\r\nX-Injected: 1",
        ["NUL"] = "a\0b",
        ["QUERY"] = "a+b&c=d"
    };

    [Fact]
    public void Names_ReturnsDistinctCaseInsensitive()
    {
        var names = SecretReference.Names("${secret:ERP-KEY}/${secret:erp-key}/${secret:DB-PASSWORD}");

        Assert.Equal(2, names.Count);
    }

    [Fact]
    public void Names_NoReference_IsEmpty() => Assert.Empty(SecretReference.Names("plain value"));

    [Theory]
    [InlineData("${secret:ERP-KEY}", "k1")]
    [InlineData("${secret:erp-key}", "k1")]
    [InlineData("literal", "literal")]
    public void ResolveExact_WholeField(string template, string expected)
    {
        var result = SecretReference.ResolveExact(template, Values);

        Assert.True(result.Succeeded);
        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public void ResolveExact_PartialReference_IsInvalid() =>
        Assert.Equal(SecretFailure.InvalidReference, SecretReference.ResolveExact("x${secret:ERP-KEY}", Values).Failure);

    [Fact]
    public void Resolve_UnknownName_IsMissing()
    {
        var result = SecretReference.ResolveHeader("${secret:NOPE}", Values);

        Assert.Equal(SecretFailure.Missing, result.Failure);
        Assert.Equal("NOPE", result.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_EmptyValue_IsMissing(string value)
    {
        var values = new Dictionary<string, string> { ["EMPTY"] = value };

        Assert.Equal(SecretFailure.Missing, SecretReference.ResolveExact("${secret:EMPTY}", values).Failure);
    }

    [Theory]
    [InlineData("${secret:bad name}")]
    [InlineData("${secret:}")]
    [InlineData("${secret:" + "A123456789A123456789A123456789A123456789A123456789A123456789A12345}")]
    public void Resolve_InvalidName_IsInvalidReference(string template) =>
        Assert.Equal(SecretFailure.InvalidReference, SecretReference.ResolveHeader(template, Values).Failure);

    [Fact]
    public void Resolve_DoesNotResolveRecursively()
    {
        var result = SecretReference.ResolveExact("${secret:NESTED}", Values);

        Assert.Equal("${secret:ERP-KEY}", result.Value);
    }

    [Fact]
    public void ResolveHeader_SubstitutesInsideValue() =>
        Assert.Equal("Key k1", SecretReference.ResolveHeader("Key ${secret:ERP-KEY}", Values).Value);

    [Theory]
    [InlineData("${secret:CRLF}")]
    [InlineData("${secret:NUL}")]
    public void Header_SecretWithCrLf_FailsClosed(string template) =>
        Assert.Equal(SecretFailure.InvalidCharacter, SecretReference.ResolveHeader(template, Values).Failure);

    [Fact]
    public void ResolveQueryValue_EscapesValue() =>
        Assert.Equal("a%2Bb%26c%3Dd", SecretReference.ResolveQueryValue("${secret:QUERY}", Values).Value);

    [Fact]
    public void ConnectionString_SecretWithSemicolon_AddsNoKeyword()
    {
        var result = SecretReference.ResolveConnectionString("Server=sql;Database=app;Password=${secret:SEMI}", Values);

        var builder = new DbConnectionStringBuilder { ConnectionString = result.Value };
        Assert.True(result.Succeeded);
        Assert.Equal("app", builder["Database"]);
        Assert.Equal("x;Database=evil", builder["Password"]);
    }

    [Fact]
    public void ConnectionString_WithoutReference_IsUnchanged()
    {
        const string literal = "Server=sql;Database=app;User ID=svc;Password=p";

        Assert.Equal(literal, SecretReference.ResolveConnectionString(literal, Values).Value);
    }
}
