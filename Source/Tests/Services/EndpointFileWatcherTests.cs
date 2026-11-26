using PortwayApi.Classes;
using PortwayApi.Services.Configuration;
using Xunit;

namespace PortwayApi.Tests.Services;

/// <summary>
/// Reload log endpoint identity
/// </summary>
public class EndpointFileWatcherTests
{
    [Theory]
    [InlineData("/app/endpoints/Proxy/CRM/Accounts/v2/entity.json", "CRM/Accounts@v2")]
    [InlineData("/app/endpoints/Proxy/CRM/Accounts/entity.json", "CRM/Accounts")]
    [InlineData("/app/endpoints/SQL/Products/entity.json", "Products")]
    [InlineData(@"C:\portway\endpoints\SQL\Warehouse\Warehouses\v3\entity.json", "Warehouse/Warehouses@v3")]
    [InlineData("/app/endpoints/Static/Docs/readme.json", "Docs/readme")]
    public void DescribeEndpoint_NamesIdentity(string path, string expected) =>
        Assert.Equal(expected, EndpointFileWatcher.DescribeEndpoint(path));

    [Theory]
    [InlineData("/app/endpoints/SQL/entity.json")]
    [InlineData("/app/other/SQL/Products/entity.json")]
    [InlineData("")]
    public void DescribeEndpoint_OutsideAnEndpointFolder_IsNull(string path) =>
        Assert.Null(EndpointFileWatcher.DescribeEndpoint(path));

    [Theory]
    [InlineData("/app/endpoints/Proxy/CRM/Accounts/v2/entity.json", EndpointType.Proxy)]
    [InlineData(@"C:\portway\endpoints\Webhooks\Hooks\entity.json", EndpointType.Webhook)]
    [InlineData("/srv/endpoints/SQL-gw/endpoints/Proxy/A/entity.json", EndpointType.Proxy)]
    [InlineData("/srv/endpoints/Static/endpoints/SQL/A/entity.json", EndpointType.SQL)]
    public void EndpointType_IsReadFromTheSameFolderAsTheName(string path, EndpointType expected) =>
        Assert.Equal(expected, EndpointHandler.GetEndpointTypeFromPath(path));

    [Theory]
    [InlineData("/app/other/SQL/Products/entity.json")]
    [InlineData("/app/endpoints/Unknown/Products/entity.json")]
    [InlineData("")]
    public void EndpointType_OutsideAnEndpointFolder_IsNull(string path) =>
        Assert.Null(EndpointHandler.GetEndpointTypeFromPath(path));
}
