using PortwayApi.Classes;
using PortwayApi.Classes.OpenApi;
using PortwayApi.Services.Database;
using PortwayApi.Tests.Base;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

/// <summary>
/// The OpenAPI document describes resources, never the database or upstream behind them
/// </summary>
public class OpenApiDisclosureTests : ApiTestBase
{
    private static readonly string[] InfrastructureTerms =
        ["proxies", "proxied", "webservice", "upstream", "stored procedure", "SQL Type", "Function parameter"];

    [Fact]
    public async Task Document_NamesNoBackingInfrastructure()
    {
        SetAllowedEnvironments("500", "700", "Synergy", "WMS");

        var response = await _client.GetAsync("/docs/openapi.json", TestContext.Current.CancellationToken);
        var document = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        foreach (var term in InfrastructureTerms)
            Assert.DoesNotContain(term, document, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(EndpointType.Standard)]
    [InlineData(EndpointType.SQL)]
    public void DefaultDescription_UsesPublicNameOnly(EndpointType type)
    {
        var definition = new EndpointDefinition { Type = type, DatabaseObjectName = "tbl_InternalItems" };

        var description = DynamicEndpointDocumentFilter.GetOperationDescription("GET", "Products", definition);

        Assert.Contains("Products", description);
        Assert.DoesNotContain("tbl_InternalItems", description);
        Assert.DoesNotContain("webservice", description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ColumnDescription_OmitsDatabaseType()
    {
        var column = new ColumnMetadata { ColumnName = "Code", DataType = "nvarchar", MaxLength = 20 };

        var description = SqlMetadataDocumentFilter.BuildColumnDescription(column);

        Assert.Equal("Max Length: 20", description);
    }

    [Fact]
    public void ParameterDescription_OmitsDatabaseTypeAndPosition()
    {
        var parameter = new ParameterMetadata { ParameterName = "@Code", DataType = "nvarchar", Position = 3, IsOutput = true };

        var description = SqlMetadataDocumentFilter.BuildParameterDescription(parameter);

        Assert.Equal("**Output**", description);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("MERGE")]
    public void RequestSchemaDescription_DoesNotNameProcedure(string method) =>
        Assert.DoesNotContain("procedure", SqlMetadataDocumentFilter.GetSchemaDescription(method), StringComparison.OrdinalIgnoreCase);
}
