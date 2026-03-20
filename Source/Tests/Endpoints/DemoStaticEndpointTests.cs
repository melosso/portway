using PortwayApi.Tests.Base;
using System.Net;
using System.Text;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

/// <summary>
/// Integration tests for the demo Static endpoint: Masterdata/CostCenters.
///
/// Config: endpoints/Static/Masterdata/CostCenters/entity.json
///   - ContentType: application/json
///   - ContentFile: costcenters-2025.json
///   - EnableFiltering: true
///   - AllowedEnvironments: ["500", "700", "Synergy"]
/// </summary>
public class DemoStaticEndpointTests : ApiTestBase
{
    private const string ValidEnv = "500";
    private const string EndpointPath = "Masterdata/CostCenters";
    private const string ApiPath = $"/api/{ValidEnv}/{EndpointPath}";

    public DemoStaticEndpointTests()
    {
        SetAllowedEnvironments("500", "700", "Synergy");
    }

    [Fact]
    public async Task GetCostCenters_ValidEnvironment_NotUnauthorizedOrBadRequest()
    {
        // Act: content file may not exist in test environment → 404 is acceptable
        var response = await _client.GetAsync(ApiPath);

        // Assert
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetCostCenters_SynergyEnvironment_NotUnauthorizedOrBadRequest()
    {
        // Arrange: Synergy is also in AllowedEnvironments for this endpoint
        SetAllowedEnvironments("500", "700", "Synergy");

        // Act
        var response = await _client.GetAsync($"/api/Synergy/{EndpointPath}");

        // Assert
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetCostCenters_EnvironmentNotInEndpointAllowedList_ReturnsBadRequest()
    {
        // Arrange: WMS is globally allowed but CostCenters only permits 500, 700, Synergy
        SetAllowedEnvironments("500", "700", "Synergy", "WMS");

        // Act
        var response = await _client.GetAsync($"/api/WMS/{EndpointPath}");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetCostCenters_GloballyDisallowedEnvironment_ReturnsBadRequest()
    {
        // Act
        var response = await _client.GetAsync("/api/invalid/Masterdata/CostCenters");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetCostCenters_NoAuthToken_ReturnsUnauthorized()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await _client.GetAsync(ApiPath);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostCostCenters_StaticEndpointDoesNotSupportPost_ReturnsNotFound()
    {
        // Arrange: static endpoints are read-only; POST falls through to 404
        var body = new StringContent("{}", Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync(ApiPath, body);

        // Assert: static endpoints have no POST handler → not found
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetCostCenters_WithODataFilter_NotUnauthorizedOrBadRequest()
    {
        // Arrange: EnableFiltering: true — OData params should be accepted
        // Act
        var response = await _client.GetAsync($"{ApiPath}?$filter=Code eq 'CC100'");

        // Assert
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetCostCenters_AltValidEnvironment_NotUnauthorizedOrBadRequest()
    {
        // Arrange: 700 is also in AllowedEnvironments
        var response = await _client.GetAsync($"/api/700/{EndpointPath}");

        // Assert
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
