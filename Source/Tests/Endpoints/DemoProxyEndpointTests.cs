using PortwayApi.Tests.Base;
using System.Net;
using System.Text;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

/// <summary>
/// Integration tests for the demo Proxy endpoint: Account/Accounts.
///
/// Config: endpoints/Proxy/Account/Accounts/entity.json
///   - Url: http://localhost:8020/.../Account
///   - Methods: GET, POST, PUT, DELETE
///   - CustomProperties.HttpMethodTranslation: "PUT:MERGE"
///   - CustomProperties.HttpMethodAppendHeaders: "PUT:X-Custom-Original-Method={ORIGINAL_METHOD}"
///   - DeletePatterns: [{Style: "ODataGuid"}]
///   - No AllowedEnvironments restriction (inherits global)
/// </summary>
public class DemoProxyEndpointTests : ApiTestBase
{
    private const string ValidEnv = "500";
    private const string EndpointPath = "Account/Accounts";
    private const string ApiPath = $"/api/{ValidEnv}/{EndpointPath}";

    public DemoProxyEndpointTests()
    {
        SetAllowedEnvironments("500", "700");
    }

    [Fact]
    public async Task GetAccounts_ValidEnvironment_NotUnauthorizedOrBadRequest()
    {
        // Act
        var response = await _client.GetAsync(ApiPath);

        // Assert: auth and routing succeeded — backend failure is acceptable
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetAccounts_GloballyDisallowedEnvironment_ReturnsBadRequest()
    {
        // Act
        var response = await _client.GetAsync("/api/invalid/Account/Accounts");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetAccounts_NoAuthToken_ReturnsUnauthorized()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await _client.GetAsync(ApiPath);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostAccounts_ValidEnvironment_NotUnauthorizedOrBadRequest()
    {
        // Arrange: POST is an allowed method for this proxy endpoint
        var body = new StringContent("""{"Name":"Test Corp","Type":"Customer"}""", Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync(ApiPath, body);

        // Assert
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task PutAccounts_ValidEnvironment_NotUnauthorizedOrBadRequest()
    {
        // Arrange: PUT is an allowed method — it will be translated to MERGE by CustomProperties
        var body = new StringContent("""{"Name":"Updated Corp"}""", Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PutAsync($"{ApiPath}/guid'some-guid'", body);

        // Assert
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAccounts_ValidEnvironment_NotUnauthorizedOrBadRequest()
    {
        // Arrange: DELETE is an allowed method
        // Act
        var response = await _client.DeleteAsync($"{ApiPath}/guid'some-guid'");

        // Assert
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task GetAccounts_NoAuthToken_Returns401EvenWithValidEnvironment()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await _client.GetAsync($"/api/700/{EndpointPath}");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAccounts_AltValidEnvironment_NotUnauthorizedOrBadRequest()
    {
        // Arrange: Account/Accounts has no AllowedEnvironments restriction, so 700 is valid
        // Act
        var response = await _client.GetAsync($"/api/700/{EndpointPath}");

        // Assert
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
