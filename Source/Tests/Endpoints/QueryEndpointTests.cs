using PortwayApi.Tests.Base;
using System.Net;
using System.Text;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

/// <summary>Integration tests for the QUERY method (RFC 10008) against demo endpoints</summary>
/// <remarks>
/// Fixtures:
/// - SQL Inventory/StockLevels  : AllowedMethods ["QUERY"], envs 500/700 (QUERY-only read endpoint)
/// - SQL WMS/Warehouses         : AllowedMethods ["GET"],   env WMS
/// - Proxy Account/Accounts     : Methods GET/POST/PUT/DELETE (no QUERY), env 500
/// - Static Masterdata/CostCenters : env 500/700/Synergy
/// </remarks>
public class QueryEndpointTests : ApiTestBase
{
    private static HttpRequestMessage Query(string url, string json = "{}") =>
        new(new HttpMethod("QUERY"), url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    // A QUERY-only SQL endpoint accepts QUERY: it passes method gating and reaches the read path
    [Fact]
    public async Task Query_SqlEndpointAllowingQuery_IsAccepted()
    {
        SetAllowedEnvironments("500", "700");

        var response = await _client.SendAsync(Query("/api/500/Inventory/StockLevels", "{\"filter\":\"Quantity gt 0\"}"));

        // Not gated out (405), not an auth/format error; a backend failure (500) is acceptable in the mocked test host
        Assert.NotEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    // The same QUERY-only endpoint rejects GET, because GET is not in its AllowedMethods
    [Fact]
    public async Task Get_QueryOnlySqlEndpoint_Returns405()
    {
        SetAllowedEnvironments("500", "700");

        var response = await _client.GetAsync("/api/500/Inventory/StockLevels");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    // A GET-only SQL endpoint rejects QUERY with 405 (gating happens before any query runs)
    [Fact]
    public async Task Query_SqlEndpointWithoutQueryMethod_Returns405()
    {
        SetAllowedEnvironments("WMS");

        var response = await _client.SendAsync(Query("/api/WMS/WMS/Warehouses", "{\"filter\":\"Id eq 1\"}"));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    // A proxy endpoint whose Methods omit QUERY rejects it with 405 before any upstream call
    [Fact]
    public async Task Query_ProxyEndpointWithoutQueryMethod_Returns405()
    {
        SetAllowedEnvironments("500", "700");

        var response = await _client.SendAsync(Query("/api/500/Account/Accounts"));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    // QUERY on a Static endpoint routes to the read path (content served, or 404 when the file is absent), never an auth/format/gating error
    [Fact]
    public async Task Query_StaticEndpoint_RoutesToReadPath()
    {
        SetAllowedEnvironments("500", "700", "Synergy");

        var response = await _client.SendAsync(Query("/api/500/Masterdata/CostCenters"));

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }
}
