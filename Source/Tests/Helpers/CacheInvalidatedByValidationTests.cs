using System.Text.Json;
using PortwayApi.Classes;
using Xunit;

namespace PortwayApi.Tests.Helpers;

/// <summary>
/// CacheInvalidatedBy validation applied by console saves and endpoint loading
/// </summary>
public sealed class CacheInvalidatedByValidationTests
{
    private static IReadOnlyList<string> Validate(object cacheInvalidatedBy) =>
        EndpointHandler.ValidateConfig(EndpointType.SQL, JsonSerializer.Serialize(new
        {
            DatabaseObjectName = "ItemsView",
            DatabaseObjectType = "View",
            AllowedColumns = new[] { "Id" },
            Properties = new { CacheInvalidatedBy = cacheInvalidatedBy }
        }));

    [Theory]
    [InlineData("Items")]
    [InlineData("dbo.Items")]
    [InlineData("sales_2026.Order_Lines")]
    public void TableName_IsAccepted(string table) => Assert.Empty(Validate(new[] { table }));

    [Theory]
    [InlineData("Items; DROP TABLE Items")]
    [InlineData("a.b.c")]
    [InlineData("[dbo].[Items]")]
    [InlineData("")]
    public void NonTableName_IsRejected(string table) =>
        Assert.Contains(Validate(new[] { table }), e => e.Contains("CacheInvalidatedBy"));

    [Fact]
    public void NonArray_IsRejected() =>
        Assert.Contains(Validate("Items"), e => e.Contains("CacheInvalidatedBy"));

    [Fact]
    public void NonStringEntry_IsRejected() =>
        Assert.Contains(Validate(new object[] { 5 }), e => e.Contains("CacheInvalidatedBy"));
}
