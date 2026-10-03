using PortwayApi.Tests.Base;
using System.Net;
using System.Text;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

/// <summary>
/// Demo proxy endpoint CRM/Accounts (endpoints/Proxy/CRM/Accounts/entity.json)
/// </summary>
public class DemoProxyEndpointTests : ApiTestBase
{
    private const string ValidEnv = "500";
    private const string EndpointPath = "CRM/Accounts";
    private const string ApiPath = $"/api/{ValidEnv}/{EndpointPath}";

    public DemoProxyEndpointTests()
    {
        SetAllowedEnvironments("500", "700");
    }

    [Fact]
    public async Task GetAccounts_ValidEnvironment_NotUnauthorizedOrBadRequest()
    {
        // Act
        var response = await _client.GetAsync(ApiPath, TestContext.Current.CancellationToken);

        // Assert: auth and routing succeeded; backend failure is acceptable
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetAccounts_GloballyDisallowedEnvironment_ReturnsBadRequest()
    {
        // Act
        var response = await _client.GetAsync("/api/invalid/CRM/Accounts", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetAccounts_NoAuthToken_ReturnsUnauthorized()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await _client.GetAsync(ApiPath, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostAccounts_ValidEnvironment_NotUnauthorizedOrBadRequest()
    {
        // Arrange: POST is an allowed method for this proxy endpoint
        var body = new StringContent("""{"Name":"Test Corp","Type":"Customer"}""", Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync(ApiPath, body, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task PutAccounts_ValidEnvironment_NotUnauthorizedOrBadRequest()
    {
        // Arrange: PUT is an allowed method; it will be translated to MERGE by CustomProperties
        var body = new StringContent("""{"Name":"Updated Corp"}""", Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PutAsync($"{ApiPath}/guid'some-guid'", body, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAccounts_ValidEnvironment_NotUnauthorizedOrBadRequest()
    {
        // Arrange: DELETE is an allowed method; Act
        var response = await _client.DeleteAsync($"{ApiPath}/guid'some-guid'", TestContext.Current.CancellationToken);

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
        var response = await _client.GetAsync($"/api/700/{EndpointPath}", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAccounts_AltValidEnvironment_NotUnauthorizedOrBadRequest()
    {
        // Arrange: CRM/Accounts has no AllowedEnvironments restriction, so 700 is valid; Act
        var response = await _client.GetAsync($"/api/700/{EndpointPath}", TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
