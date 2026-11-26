using PortwayApi.Tests.Support;
using PortwayApi.Tests.Base;
using Xunit;

namespace PortwayApi.Tests.Middleware;

public sealed class ContentGateResolutionTests : ApiTestBase, IDisposable
{
    private const string EndpointDir = "endpoints/Proxy/Inventory";

    public ContentGateResolutionTests()
    {
        SetAllowedEnvironments("500", "700");
        Directory.CreateDirectory(EndpointDir);
        File.WriteAllText(Path.Combine(EndpointDir, "entity.json"), """
            {
              "Url": "http://localhost:8023/inventory",
              "Methods": ["GET", "POST"]
            }
            """);
        PortwayApi.Classes.EndpointHandler.ReloadAllEndpoints();
    }

    [Fact]
    public async Task XmlPost_ToSqlEndpointUnderProxyName_Returns415()
    {
        var response = await _client.PostAsync("/api/700/Inventory/Products", new StringContent("<item/>", System.Text.Encoding.UTF8, "application/xml"), TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    public new void Dispose()
    {
        TempDirectory.TryDelete(EndpointDir);
        PortwayApi.Classes.EndpointHandler.ReloadAllEndpoints();
        base.Dispose();
    }
}
