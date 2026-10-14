using PortwayApi.Classes;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

public class EndpointEntityParsingTests
{
    [Fact]
    public void ProxyEntity_LoadsFailoverRetryAndTransforms()
    {
        var definition = EndpointHandler.ParseProxyEndpointDefinition("""
            {
              "Url": "http://primary/api/orders",
              "Methods": ["GET"],
              "FallbackUrls": ["http://standby/api/orders"],
              "Retry": { "Attempts": 3, "DelayMs": 50 },
              "ResponseTransforms": { "Rename": { "sku": "itemCode" }, "Remove": ["internal"], "Mask": ["iban"] }
            }
            """);

        Assert.NotNull(definition);
        Assert.Equal(["http://standby/api/orders"], definition.FallbackUrls);
        Assert.Equal(3, definition.Retry?.Attempts);
        Assert.Equal(50, definition.Retry?.DelayMs);
        Assert.Equal("itemCode", definition.ResponseTransforms?.Rename?["sku"]);
        Assert.Equal(["internal"], definition.ResponseTransforms?.Remove);
        Assert.Equal(["iban"], definition.ResponseTransforms?.Mask);
    }

    [Fact]
    public void SqlEntity_LoadsTransforms()
    {
        var definition = EndpointHandler.ParseSqlEndpointDefinition("""
            {
              "DatabaseObjectName": "Items",
              "ResponseTransforms": { "Mask": ["Cost"] }
            }
            """);

        Assert.NotNull(definition);
        Assert.Equal(["Cost"], definition.ResponseTransforms?.Mask);
    }
}
