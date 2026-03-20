using PortwayApi.Tests.Base;
using System.Net;
using System.Text;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

/// <summary>
/// Integration tests for the demo Composite endpoint: Financial/SalesInvoice.
///
/// Config: endpoints/Proxy/Financial/SalesInvoice/entity.json
///   - Type: Composite
///   - Methods: ["POST"]
///   - Namespace: Financial
///   - CompositeConfig.Steps:
///       1. CreateInvoiceLines (POST InvoiceLine array, TemplateTransformations: InvoiceID → $guid)
///       2. CreateInvoiceHeader (POST InvoiceHeader, TemplateTransformations: InvoiceID → $prev.CreateInvoiceLines.0.d.InvoiceID)
///   - AllowedEnvironments: ["500", "700"]
/// </summary>
public class DemoCompositeEndpointTests : ApiTestBase
{
    private const string ValidEnv = "500";
    private const string EndpointPath = "Financial/SalesInvoice";
    private const string ApiPath = $"/api/{ValidEnv}/{EndpointPath}";

    private static readonly StringContent ValidInvoiceBody = new(
        """
        {
          "Header": { "CustomerCode": "C001", "InvoiceDate": "2026-01-01" },
          "Lines": [
            { "ItemCode": "P001", "Quantity": 2, "Price": 100.00 }
          ]
        }
        """,
        Encoding.UTF8,
        "application/json");

    public DemoCompositeEndpointTests()
    {
        SetAllowedEnvironments("500", "700");
    }

    [Fact]
    public async Task PostSalesInvoice_ValidEnvironment_NotUnauthorizedOrBadRequest()
    {
        // Act: composite handler will attempt to call the backend; failure is acceptable
        var response = await _client.PostAsync(ApiPath, ValidInvoiceBody);

        // Assert
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostSalesInvoice_AltValidEnvironment_NotUnauthorizedOrBadRequest()
    {
        // Arrange: 700 is also in AllowedEnvironments
        var response = await _client.PostAsync($"/api/700/{EndpointPath}", ValidInvoiceBody);

        // Assert
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostSalesInvoice_EnvironmentNotInAllowedList_ReturnsBadRequest()
    {
        // Arrange: Synergy is globally allowed but Financial/SalesInvoice only permits 500, 700
        SetAllowedEnvironments("500", "700", "Synergy");

        // Act
        var response = await _client.PostAsync($"/api/Synergy/{EndpointPath}", ValidInvoiceBody);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostSalesInvoice_GloballyDisallowedEnvironment_ReturnsBadRequest()
    {
        // Act
        var response = await _client.PostAsync($"/api/invalid/{EndpointPath}", ValidInvoiceBody);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostSalesInvoice_NoAuthToken_ReturnsUnauthorized()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await _client.PostAsync(ApiPath, ValidInvoiceBody);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetSalesInvoice_CompositeEndpointGetNotSupported_ReturnsMethodNotAllowed()
    {
        // Arrange: composite endpoints only support POST; GET must return 405
        // Act
        var response = await _client.GetAsync(ApiPath);

        // Assert
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task DeleteSalesInvoice_CompositeEndpointDeleteNotSupported_ReturnsMethodNotAllowed()
    {
        // Act
        var response = await _client.DeleteAsync($"{ApiPath}/some-id");

        // Assert
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }
}
