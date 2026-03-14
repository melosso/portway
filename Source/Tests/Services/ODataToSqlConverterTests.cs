using Microsoft.OData.Edm;
using Moq;
using PortwayApi.Classes;
using PortwayApi.Classes.Providers;
using PortwayApi.Interfaces;
using PortwayApi.Services.Providers;
using SqlKata.Compilers;
using Xunit;

namespace PortwayApi.Tests.Services;

public class ODataToSqlConverterTests
{
    private readonly ODataToSqlConverter _converter;
    private readonly Mock<IEdmModelBuilder> _mockEdmModelBuilder;

    public ODataToSqlConverterTests()
    {
        _mockEdmModelBuilder = new Mock<IEdmModelBuilder>();

        // Create a simple EDM model for testing
        var mockModel = new Mock<IEdmModel>();
        _mockEdmModelBuilder.Setup(m => m.GetEdmModel(It.IsAny<string>())).Returns(mockModel.Object);

        // Provide all registered providers (mirroring Program.cs registration)
        var providers = new ISqlProvider[]
        {
            new MsSqlProvider(),
            new PostgreSqlProvider(),
            new MySqlProvider(),
            new SqliteProvider()
        };

        _converter = new ODataToSqlConverter(_mockEdmModelBuilder.Object, providers);
    }
    
    [Fact]
    public void ConvertToSQL_BasicSelect_GeneratesCorrectSql()
    {
        // Arrange
        string entityName = "dbo.Items"; // Use the actual database object name (Items), not the endpoint name
        var odataParams = new Dictionary<string, string>
        {
            { "select", "ItemCode,Description" }
        };
        
        // Act
        var (sqlQuery, parameters) = _converter.ConvertToSQL(entityName, odataParams);
        
        // Assert
        Assert.NotNull(sqlQuery);
        Assert.Contains("SELECT", sqlQuery);
        Assert.Contains("[ItemCode]", sqlQuery);
        Assert.Contains("[Description]", sqlQuery);
        Assert.Contains("FROM", sqlQuery);
        Assert.Contains("[dbo].[Items]", sqlQuery); // Check for Items, not Products
    }
    
    [Fact]
    public void ConvertToSQL_WithFilter_GeneratesWhereClause()
    {
        // Arrange
        string entityName = "dbo.Items"; // Use the actual database object name
        var odataParams = new Dictionary<string, string>
        {
            { "filter", "ItemCode eq 'TEST001'" }
        };
        
        // Act
        var (sqlQuery, parameters) = _converter.ConvertToSQL(entityName, odataParams);
        
        // Assert
        Assert.NotNull(sqlQuery);
        Assert.Contains("WHERE", sqlQuery);
        Assert.True(parameters.Count > 0);
    }
    
    [Fact]
    public void ConvertToSQL_WithOrderBy_GeneratesOrderByClause()
    {
        // Arrange
        string entityName = "dbo.Items"; // Use the actual database object name
        var odataParams = new Dictionary<string, string>
        {
            { "orderby", "Description desc" }
        };
        
        // Act
        var (sqlQuery, parameters) = _converter.ConvertToSQL(entityName, odataParams);
        
        // Assert
        Assert.NotNull(sqlQuery);
        Assert.Contains("ORDER BY", sqlQuery);
        Assert.Contains("DESC", sqlQuery);
    }
}
