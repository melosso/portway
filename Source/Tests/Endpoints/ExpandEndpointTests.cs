using PortwayApi.Tests.Base;
using System.Net;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

/// <summary>Integration tests for the $expand read-path gates and proxy passthrough</summary>
/// <remarks>
/// Uses demo endpoints: WMS/Warehouses (SQL Table, no relationships), Company/Departments (TVF),
/// Account/Accounts (Proxy). None declares a relationship, so any $expand on the SQL table is unknown.
/// </remarks>
public class ExpandEndpointTests : ApiTestBase
{
    public ExpandEndpointTests()
    {
        SetAllowedEnvironments("WMS", "500", "700");
    }

    [Fact]
    public async Task Sql_UnknownExpand_ReturnsBadRequest()
    {
        var response = await _client.GetAsync("/api/WMS/WMS/Warehouses?$expand=Category");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Sql_NestedExpandOptions_ReturnsBadRequest()
    {
        var response = await _client.GetAsync("/api/WMS/WMS/Warehouses?$expand=Category($select=Name)");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Tvf_Expand_ReturnsBadRequest()
    {
        var response = await _client.GetAsync("/api/500/Company/Departments?$expand=Foo");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Proxy_Expand_PassesThrough_NotBadRequest()
    {
        // Portway must never parse or reject $expand on a proxy; the upstream owns it.
        // The demo upstream is unreachable, so a bad gateway or timeout is fine, a 400 is not
        var response = await _client.GetAsync("/api/500/Account/Accounts?$expand=Lines");

        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
