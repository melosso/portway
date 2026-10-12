using PortwayApi.Tests.Base;
using System.Net;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

/// <summary>
/// $expand read-path gates and proxy passthrough on demo endpoints without relationships
/// </summary>
public class ExpandEndpointTests : ApiTestBase
{
    public ExpandEndpointTests()
    {
        SetAllowedEnvironments("WMS", "500", "700");
    }

    [Fact]
    public async Task Sql_UnknownExpand_ReturnsBadRequest()
    {
        var response = await _client.GetAsync("/api/WMS/WMS/Warehouses?$expand=Category", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Sql_NestedExpandOptions_ReturnsBadRequest()
    {
        var response = await _client.GetAsync("/api/WMS/WMS/Warehouses?$expand=Category($select=Name)", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Tvf_Expand_ReturnsBadRequest()
    {
        var response = await _client.GetAsync("/api/500/Company/Departments?$expand=Foo", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Proxy_Expand_PassesThrough_NotBadRequest()
    {
        // Proxy $expand belongs to the upstream; anything but 400 passes
        var response = await _client.GetAsync("/api/500/CRM/Accounts?$expand=Lines", TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
