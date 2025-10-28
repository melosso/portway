using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Moq;
using PortwayApi.Classes;
using PortwayApi.Classes.Helpers;
using System.Collections.Specialized;
using Xunit;

namespace PortwayApi.Tests.Helpers;

public class TableValuedFunctionHelperTests
{
    [Fact]
    public void IsTableValuedFunction_WithTVFType_ReturnsTrue()
    {
        // Arrange
        var endpoint = new EndpointDefinition
        {
            DatabaseObjectType = "TableValuedFunction"
        };

        // Act
        var result = TableValuedFunctionHelper.IsTableValuedFunction(endpoint);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsTableValuedFunction_WithTableType_ReturnsFalse()
    {
        // Arrange
        var endpoint = new EndpointDefinition
        {
            DatabaseObjectType = "Table"
        };

        // Act
        var result = TableValuedFunctionHelper.IsTableValuedFunction(endpoint);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsTableValuedFunction_WithNullType_ReturnsFalse()
    {
        // Arrange
        var endpoint = new EndpointDefinition
        {
            DatabaseObjectType = null
        };

        // Act
        var result = TableValuedFunctionHelper.IsTableValuedFunction(endpoint);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ExtractParameterValues_PathParameter_ExtractsCorrectly()
    {
        // Arrange
        var parameters = new List<TVFParameter>
        {
            new TVFParameter
            {
                Name = "CustomerId",
                SqlType = "int",
                Source = "Path",
                Position = 1,
                Required = true
            }
        };

        var mockRequest = new Mock<HttpRequest>();
        var pathSegments = new[] { "12345" };

        // Act
        var result = TableValuedFunctionHelper.ExtractParameterValues(parameters, mockRequest.Object, pathSegments);

        // Assert
        Assert.Empty(result.Errors);
        Assert.Single(result.Parameters);
        Assert.Equal("CustomerId", result.Parameters.First().Key);
        Assert.Equal("12345", result.Parameters.First().Value);
    }

    [Fact]
    public void ExtractParameterValues_QueryParameter_ExtractsCorrectly()
    {
        // Arrange
        var parameters = new List<TVFParameter>
        {
            new TVFParameter
            {
                Name = "StartDate",
                SqlType = "datetime",
                Source = "Query",
                Required = false,
                DefaultValue = "2024-01-01"
            }
        };

        var mockRequest = new Mock<HttpRequest>();
        var mockQuery = new Mock<IQueryCollection>();
        
        mockQuery.Setup(q => q.TryGetValue("StartDate", out It.Ref<StringValues>.IsAny))
            .Returns((string key, out StringValues values) =>
            {
                values = new StringValues("2024-06-01");
                return true;
            });
        mockRequest.Setup(r => r.Query).Returns(mockQuery.Object);

        var pathSegments = new string[0];

        // Act
        var result = TableValuedFunctionHelper.ExtractParameterValues(parameters, mockRequest.Object, pathSegments);

        // Assert
        Assert.Empty(result.Errors);
        Assert.Single(result.Parameters);
        Assert.Equal("StartDate", result.Parameters.First().Key);
        Assert.Equal("2024-06-01", result.Parameters.First().Value);
    }

    [Fact]
    public void ExtractParameterValues_HeaderParameter_ExtractsCorrectly()
    {
        // Arrange
        var parameters = new List<TVFParameter>
        {
            new TVFParameter
            {
                Name = "ReportType", 
                SqlType = "nvarchar",
                Source = "Header",
                Required = true
            }
        };

        var mockRequest = new Mock<HttpRequest>();
        var mockHeaders = new Mock<IHeaderDictionary>();
        
        mockHeaders.Setup(h => h.TryGetValue("ReportType", out It.Ref<StringValues>.IsAny))
            .Returns((string key, out StringValues values) =>
            {
                values = new StringValues("activity");
                return true;
            });
        mockRequest.Setup(r => r.Headers).Returns(mockHeaders.Object);

        var pathSegments = new string[0];

        // Act
        var result = TableValuedFunctionHelper.ExtractParameterValues(parameters, mockRequest.Object, pathSegments);

        // Assert
        Assert.Empty(result.Errors);
        Assert.Single(result.Parameters);
        Assert.Equal("ReportType", result.Parameters.First().Key);
        Assert.Equal("activity", result.Parameters.First().Value);
    }

    [Fact]
    public void ExtractParameterValues_MissingRequiredParameter_ReturnsError()
    {
        // Arrange
        var parameters = new List<TVFParameter>
        {
            new TVFParameter
            {
                Name = "CustomerId",
                SqlType = "int", 
                Source = "Path",
                Position = 1,
                Required = true
            }
        };

        var mockRequest = new Mock<HttpRequest>();
        var pathSegments = new string[0]; // No path segments provided

        // Act
        var result = TableValuedFunctionHelper.ExtractParameterValues(parameters, mockRequest.Object, pathSegments);

        // Assert
        Assert.NotEmpty(result.Errors);
        Assert.Contains("Required parameter 'CustomerId' not provided", result.Errors);
    }

    [Fact]
    public void ExtractParameterValues_UsesDefaultValue_WhenParameterMissing()
    {
        // Arrange
        var parameters = new List<TVFParameter>
        {
            new TVFParameter
            {
                Name = "StartDate",
                SqlType = "datetime",
                Source = "Query", 
                Required = false,
                DefaultValue = "2024-01-01"
            }
        };

        var mockRequest = new Mock<HttpRequest>();
        var mockQuery = new Mock<IQueryCollection>();
        
        mockQuery.Setup(q => q.TryGetValue("StartDate", out It.Ref<StringValues>.IsAny))
            .Returns(false);
        mockRequest.Setup(r => r.Query).Returns(mockQuery.Object);

        var pathSegments = new string[0];

        // Act
        var result = TableValuedFunctionHelper.ExtractParameterValues(parameters, mockRequest.Object, pathSegments);

        // Assert
        Assert.Empty(result.Errors);
        Assert.Single(result.Parameters);
        Assert.Equal("StartDate", result.Parameters.First().Key);
        Assert.Equal("2024-01-01", result.Parameters.First().Value);
    }

    [Fact]
    public void BuildFunctionCall_GeneratesCorrectSQL()
    {
        // Arrange
        var functionName = "GetCustomerOrders";
        var schema = "dbo";
        var parameterValues = new Dictionary<string, object>
        {
            { "CustomerId", "12345" },
            { "StartDate", "2024-06-01" }
        };
        var functionParameters = new List<TVFParameter>
        {
            new TVFParameter { Name = "CustomerId", SqlType = "int" },
            new TVFParameter { Name = "StartDate", SqlType = "datetime" }
        };

        // Act
        var result = TableValuedFunctionHelper.BuildFunctionCall(schema, functionName, parameterValues, functionParameters);

        // Assert
        Assert.StartsWith("SELECT * FROM [dbo].[GetCustomerOrders]", result.FunctionCall);
        Assert.Equal(2, result.SqlParameters.Count);
        Assert.Contains("@param0", result.SqlParameters.Keys);
        Assert.Contains("@param1", result.SqlParameters.Keys);
        // Check the parameter values exist (type conversion may occur)
        Assert.NotNull(result.SqlParameters["@param0"]);
        Assert.NotNull(result.SqlParameters["@param1"]);
    }
}
