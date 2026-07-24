using PortwayApi.Classes;
using PortwayApi.Helpers;
using Xunit;

namespace PortwayApi.Tests.Helpers;

public class OdataExpandRelationshipValidatorTests
{
    private static EndpointEntity Table(params EndpointRelationship[] rels) => new()
    {
        DatabaseObjectName = "Items",
        DatabaseObjectType = "Table",
        Relationships = rels.Length == 0 ? null : rels.ToList()
    };

    private static EndpointRelationship Valid() => new()
    {
        Name = "Category",
        Target = "Assortments",
        LocalColumn = "Assortment",
        TargetColumn = "AssortmentID",
        Multiplicity = "ToOne"
    };

    [Fact]
    public void NoRelationships_NoErrors()
        => Assert.Empty(OdataExpandRelationshipValidator.ValidateShape(Table()));

    [Fact]
    public void ValidToOne_NoErrors()
        => Assert.Empty(OdataExpandRelationshipValidator.ValidateShape(Table(Valid())));

    [Fact]
    public void NamespacedTarget_NoErrors()
    {
        var rel = Valid();
        rel.Target = "Product/Assortments";
        Assert.Empty(OdataExpandRelationshipValidator.ValidateShape(Table(rel)));
    }

    [Fact]
    public void TvfWithRelationship_Rejected()
    {
        var entity = Table(Valid());
        entity.DatabaseObjectType = "TableValuedFunction";
        var errors = OdataExpandRelationshipValidator.ValidateShape(entity);
        Assert.Contains(errors, e => e.Contains("TableValuedFunction"));
    }

    [Fact]
    public void ToMany_Rejected()
    {
        var rel = Valid();
        rel.Multiplicity = "ToMany";
        Assert.Contains(OdataExpandRelationshipValidator.ValidateShape(Table(rel)), e => e.Contains("Multiplicity"));
    }

    [Theory]
    [InlineData("1Category")]
    [InlineData("Cat egory")]
    [InlineData("Cat;egory")]
    [InlineData("")]
    public void UnsafeName_Rejected(string name)
    {
        var rel = Valid();
        rel.Name = name;
        Assert.Contains(OdataExpandRelationshipValidator.ValidateShape(Table(rel)), e => e.Contains("Name"));
    }

    [Theory]
    [InlineData("DROP TABLE")]
    [InlineData("a-b")]
    [InlineData("")]
    public void UnsafeLocalColumn_Rejected(string column)
    {
        var rel = Valid();
        rel.LocalColumn = column;
        Assert.Contains(OdataExpandRelationshipValidator.ValidateShape(Table(rel)), e => e.Contains("LocalColumn"));
    }

    [Theory]
    [InlineData("x;y")]
    [InlineData("a b")]
    [InlineData("")]
    public void UnsafeTargetColumn_Rejected(string column)
    {
        var rel = Valid();
        rel.TargetColumn = column;
        Assert.Contains(OdataExpandRelationshipValidator.ValidateShape(Table(rel)), e => e.Contains("TargetColumn"));
    }

    [Fact]
    public void InvalidTargetRef_Rejected()
    {
        var rel = Valid();
        rel.Target = "a/b/c";
        Assert.Contains(OdataExpandRelationshipValidator.ValidateShape(Table(rel)), e => e.Contains("Target"));
    }

    [Fact]
    public void DuplicateName_Rejected()
    {
        var a = Valid();
        var b = Valid();
        b.Target = "Suppliers";
        Assert.Contains(OdataExpandRelationshipValidator.ValidateShape(Table(a, b)), e => e.Contains("duplicate"));
    }
}
