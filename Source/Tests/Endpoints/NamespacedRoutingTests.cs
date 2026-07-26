using System.Net;
using System.Text;
using System.Text.Json;
using PortwayApi.Tests.Base;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

/// <summary>Pins namespaced route resolution to the correct endpoint type; probe order reordering must fail here</summary>
public class NamespacedRoutingTests : ApiTestBase
{
    // Resolves to the SQL handler, which then fails on connect rather than on routing
    [Fact]
    public async Task NamespacedSql_ResolvesToSqlHandler()
    {
        SetAllowedEnvironments("500", "700");

        var response = await _client.GetAsync("/api/500/Product/Products");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    // A namespaced static endpoint resolves to the static handler and serves its content
    [Fact]
    public async Task NamespacedStatic_ResolvesToStaticHandler()
    {
        SetAllowedEnvironments("500", "700");

        var response = await _client.GetAsync("/api/500/Production/Lines");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(await response.Content.ReadAsStringAsync());
    }

    // Resolution runs before the Enabled gate, so disabled answers 503 not 404
    [Fact]
    public async Task NamespacedStatic_WhenDisabled_Returns503()
    {
        SetAllowedEnvironments("500", "700");

        // Production/Machines ships with Enabled false
        var response = await _client.GetAsync("/api/500/Production/Machines");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    // A proxy endpoint carrying a composite config resolves as Composite, not as Proxy
    [Fact]
    public async Task NamespacedComposite_ResolvesToCompositeHandler()
    {
        SetAllowedEnvironments("500", "700");

        var response = await _client.PostAsync("/api/500/Financial/SalesInvoice",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        // Only the composite handler reports which step failed
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.TryGetProperty("step", out _),
            "Composite endpoints must resolve to the composite handler, not the proxy handler");
        Assert.True(body.RootElement.TryGetProperty("completedSteps", out _));
    }

    // Type resolution runs before method checks
    [Fact]
    public async Task NamespacedQueryOnlySql_ResolvesAndRejectsGet()
    {
        SetAllowedEnvironments("500", "700");

        var response = await _client.GetAsync("/api/500/Inventory/StockLevels");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    // Unknown namespace falls through to the single-segment parse
    [Fact]
    public async Task UnknownNamespace_FallsThroughTo404()
    {
        SetAllowedEnvironments("500");

        var response = await _client.GetAsync("/api/500/NoSuchNamespace/NoSuchEndpoint");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
