using Microsoft.Extensions.DependencyInjection;
using Moq;
using PortwayApi.Tests.Base;
using System.Net;
using System.Text.Json;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

public class SqlEndpointTests : ApiTestBase
{
    [Fact]
    public async Task GetSqlEndpoint_ValidEnvironment_ReturnsOk()
    {
        // Arrange
        string testEnv = "600";
        string endpointName = "Products";
        
        // Ensure the environment is allowed
        SetAllowedEnvironments("600", "700");
        
        // Mock ODataToSqlConverter to return a simple query
        // Note: The endpoint name is "Products" but it maps to "dbo.Items" in the database
        var mockQueryResult = ("SELECT * FROM [dbo].[Items] WHERE [ItemCode] = @p0", 
            new Dictionary<string, object> { { "p0", "TEST001" } });
            
        _mockODataToSqlConverter
            .Setup(c => c.ConvertToSQL(It.Is<string>(s => s.Contains("Items")), It.IsAny<Dictionary<string, string>>()))
            .Returns(mockQueryResult);
        
        // Act
        var response = await _client.GetAsync($"/api/{testEnv}/{endpointName}?$filter=ItemCode eq 'TEST001'");
        
        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        // Verify that the converter was called with the correct database object name
        _mockODataToSqlConverter.Verify(
            c => c.ConvertToSQL(
                It.Is<string>(s => s.Contains("Items")), 
                It.Is<Dictionary<string, string>>(d => d.ContainsKey("filter"))),
            Times.Once);
    }
    
    [Fact]
    public async Task GetSqlEndpoint_InvalidEnvironment_ReturnsBadRequest()
    {
        // Arrange
        string testEnv = "invalid";
        string endpointName = "Products";
        
        // Configure allowed environments to not include the test environment
        SetAllowedEnvironments("600", "700");
        
        // Act
        var response = await _client.GetAsync($"/api/{testEnv}/{endpointName}");
        
        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
    
    [Fact]
    public async Task GetSqlEndpoint_Unauthorized_Returns401()
    {
        // Arrange
        string testEnv = "600";
        string endpointName = "Products";
        
        // Remove authorization header
        _client.DefaultRequestHeaders.Authorization = null;
        
        // Act
        var response = await _client.GetAsync($"/api/{testEnv}/{endpointName}");
        
        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
