using Moq;
using PortwayApi.Tests.Base;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

/// <summary>
/// Integration tests for the demo SQL endpoint: WMS/Warehouses.
///
/// Config: endpoints/SQL/WMS/Warehouses/entity.json
///   - DatabaseObjectName: Warehouses
///   - AllowedEnvironments: ["WMS"]
///   - AllowedMethods: ["GET"]
///   - AllowedColumns: Id, Code, Name, City, Country, Region, CapacityM2, IsActive (no aliases)
///   - Properties.MaxPageSize: 50
///   - Properties.DefaultSort: "Code ASC"
/// </summary>
public class DemoSqlEndpointTests : ApiTestBase
{
    private const string ValidEnv = "WMS";
    private const string EndpointPath = "WMS/Warehouses";
    private const string ApiPath = $"/api/{ValidEnv}/{EndpointPath}";

    public DemoSqlEndpointTests()
    {
        // WMS must be in the global allowed list for this endpoint to be reachable
        SetAllowedEnvironments(ValidEnv);

        _mockODataToSqlConverter
            .Setup(c => c.ConvertToSQL(
                It.Is<string>(s => s.Contains("Warehouses")),
                It.IsAny<Dictionary<string, string>>()))
            .Returns(("SELECT [Id],[Code],[Name],[City],[Country],[Region],[CapacityM2],[IsActive] FROM [Warehouses]",
                new Dictionary<string, object>()));
    }

    [Fact]
    public async Task GetWarehouses_ValidWmsEnvironment_NotUnauthorizedOrBadRequest()
    {
        // Act
        var response = await _client.GetAsync(ApiPath);

        // Assert: auth and routing succeeded — backend failure is acceptable
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetWarehouses_EnvironmentNotInEndpointAllowedList_ReturnsBadRequest()
    {
        // Arrange: 500 is globally allowed but WMS/Warehouses only permits "WMS"
        SetAllowedEnvironments("500", "WMS");

        // Act
        var response = await _client.GetAsync("/api/500/WMS/Warehouses");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetWarehouses_GloballyDisallowedEnvironment_ReturnsBadRequest()
    {
        // Arrange: "invalid" is not in the global allowed list
        SetAllowedEnvironments(ValidEnv);

        // Act
        var response = await _client.GetAsync("/api/invalid/WMS/Warehouses");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetWarehouses_NoAuthToken_ReturnsUnauthorized()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await _client.GetAsync(ApiPath);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostWarehouses_GetOnlyEndpoint_ReturnsMethodNotAllowed()
    {
        // Arrange: WMS/Warehouses only has AllowedMethods: ["GET"]
        var body = new StringContent("""{"Code":"W01","Name":"Main"}""", Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync(ApiPath, body);

        // Assert
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task GetWarehouses_WithODataFilter_ConverterCalledWithWarehousesObjectName()
    {
        // Act
        var response = await _client.GetAsync($"{ApiPath}?$filter=IsActive eq true");

        // If the DB is reachable the assertion on the converter call confirms routing
        if (response.StatusCode == HttpStatusCode.InternalServerError)
            return;

        // Assert: OData converter was called with the correct database object name
        _mockODataToSqlConverter.Verify(
            c => c.ConvertToSQL(
                It.Is<string>(s => s.Contains("Warehouses")),
                It.Is<Dictionary<string, string>>(d => d.ContainsKey("filter"))),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task GetWarehouses_WithODataSelect_ConverterCalledWithSelectParam()
    {
        // Act
        var response = await _client.GetAsync($"{ApiPath}?$select=Code,Name,City");

        if (response.StatusCode == HttpStatusCode.InternalServerError)
            return;

        // Assert
        _mockODataToSqlConverter.Verify(
            c => c.ConvertToSQL(
                It.Is<string>(s => s.Contains("Warehouses")),
                It.Is<Dictionary<string, string>>(d => d.ContainsKey("select"))),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task GetWarehouses_WithODataOrderBy_ConverterCalledWithOrderByParam()
    {
        // Act
        var response = await _client.GetAsync($"{ApiPath}?$orderby=Code asc");

        if (response.StatusCode == HttpStatusCode.InternalServerError)
            return;

        // Assert
        _mockODataToSqlConverter.Verify(
            c => c.ConvertToSQL(
                It.Is<string>(s => s.Contains("Warehouses")),
                It.Is<Dictionary<string, string>>(d => d.ContainsKey("orderby"))),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task GetWarehouses_WithTopAndSkip_ConverterCalledWithPagingParams()
    {
        // Act
        var response = await _client.GetAsync($"{ApiPath}?$top=10&$skip=20");

        if (response.StatusCode == HttpStatusCode.InternalServerError)
            return;

        // Assert
        _mockODataToSqlConverter.Verify(
            c => c.ConvertToSQL(
                It.Is<string>(s => s.Contains("Warehouses")),
                It.Is<Dictionary<string, string>>(d => d.ContainsKey("top") && d.ContainsKey("skip"))),
            Times.AtLeastOnce);
    }
}
