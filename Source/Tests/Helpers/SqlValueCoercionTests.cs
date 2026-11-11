using PortwayApi.Helpers;
using PortwayApi.Services.Database;
using PortwayApi.Services.Providers;
using Xunit;

namespace PortwayApi.Tests.Helpers;

public sealed class SqlValueCoercionTests
{
    private static ColumnMetadata Column(string clrType) => new() { ColumnName = "C", DatabaseColumnName = "C", ClrType = clrType };

    [Theory]
    [InlineData("System.Int32", "42", 42L)]
    [InlineData("System.Int64", "-7", -7L)]
    public void Integer_ParsesInvariant(string clrType, string text, long expected)
    {
        Assert.True(SqlValueCoercion.TryCoerce(Column(clrType), text, SqlProviderType.MySql, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("System.Int32", "abc")]
    [InlineData("System.Int64", "1.5")]
    [InlineData("System.Decimal", "12,5x")]
    [InlineData("System.DateTime", "not a date")]
    [InlineData("System.DateTimeOffset", "31-31-2026")]
    public void InvalidText_IsRefused(string clrType, string text) =>
        Assert.False(SqlValueCoercion.TryCoerce(Column(clrType), text, SqlProviderType.PostgreSql, out _));

    [Fact]
    public void Decimal_UsesInvariantSeparator()
    {
        Assert.True(SqlValueCoercion.TryCoerce(Column("System.Decimal"), "12.50", SqlProviderType.SqlServer, out var value));
        Assert.Equal(12.50m, value);
    }

    [Fact]
    public void ZoneAwareColumn_BindsUtcOffset()
    {
        Assert.True(SqlValueCoercion.TryCoerce(Column("System.DateTimeOffset"), "2026-11-11T12:00:00+02:00", SqlProviderType.PostgreSql, out var value));
        var stamp = Assert.IsType<DateTimeOffset>(value);
        Assert.Equal(TimeSpan.Zero, stamp.Offset);
        Assert.Equal(10, stamp.Hour);
    }

    [Theory]
    [InlineData("2026-11-11 10:00:00", 10)]
    [InlineData("2026-11-11T12:00:00+02:00", 10)]
    public void PlainColumn_BindsUtcWallClockUnspecified(string text, int hour)
    {
        Assert.True(SqlValueCoercion.TryCoerce(Column("System.DateTime"), text, SqlProviderType.PostgreSql, out var value));
        var stamp = Assert.IsType<DateTime>(value);
        Assert.Equal(DateTimeKind.Unspecified, stamp.Kind);
        Assert.Equal(hour, stamp.Hour);
    }

    [Fact]
    public void Sqlite_KeepsDateText()
    {
        Assert.True(SqlValueCoercion.TryCoerce(Column("System.DateTime"), "2026-11-11 10:00", SqlProviderType.Sqlite, out var value));
        Assert.Equal("2026-11-11 10:00", value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("System.String")]
    public void NoMetadataOrTextColumn_PassesUnchanged(string? clrType)
    {
        Assert.True(SqlValueCoercion.TryCoerce(clrType is null ? null : Column(clrType), "abc", SqlProviderType.MySql, out var value));
        Assert.Equal("abc", value);
    }

    [Fact]
    public void NonStringValues_PassUnchanged()
    {
        Assert.True(SqlValueCoercion.TryCoerce(Column("System.Int32"), 5, SqlProviderType.MySql, out var value));
        Assert.Equal(5, value);
    }
}
