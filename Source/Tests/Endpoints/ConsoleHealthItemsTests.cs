using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using PortwayApi.Classes;
using PortwayApi.Endpoints;
using Xunit;

using MsHealthCheckService = Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService;

namespace PortwayApi.Tests.Endpoints;

/// <summary>
/// The dashboard lists each failing environment and endpoint with a short problem and a link, never the raw driver error
/// </summary>
[Collection("Integration")]
public sealed class ConsoleHealthItemsTests : IDisposable
{
    private static readonly string Dir = Path.Combine("endpoints", "Proxy", "HealthItemsTest");

    public ConsoleHealthItemsTests()
    {
        Directory.CreateDirectory(Path.Combine(Dir, "Orders"));
        File.WriteAllText(Path.Combine(Dir, "Orders", "entity.json"), """{ "Url": "http://items.invalid/orders", "Methods": ["GET"] }""");
        EndpointHandler.ReloadAllEndpoints();
    }

    public void Dispose()
    {
        Directory.Delete(Dir, recursive: true);
        EndpointHandler.ReloadAllEndpoints();
    }

    private static HealthReportEntry Entry(HealthStatus status, Dictionary<string, object> data) =>
        new(status, "", TimeSpan.Zero, null, data);

    [Fact]
    public void SqlFailure_LinksToTheEnvironmentWithoutTheDriverError()
    {
        var items = WebUiEndpointExtensions.HealthItems("SqlConnectivity", Entry(HealthStatus.Unhealthy, new()
        {
            ["WMS"] = new { Status = "Unhealthy", Error = "DB error: Login failed for user 'sa' on server 10.0.0.5" },
            ["prod"] = new { Status = "Unhealthy", Error = "Connection timed out after 5 seconds" },
            ["test"] = new { Status = "Healthy" }
        })).ToDictionary(i => i.Name);

        Assert.Equal(new ConsoleHealthItem("WMS", "Unhealthy", "Connection failed", "environments#WMS"), items["WMS"]);
        Assert.Equal("Timed out", items["prod"].Problem);
        Assert.Equal(new ConsoleHealthItem("test", "Healthy", null, "environments#test"), items["test"]);
    }

    [Fact]
    public void ProxyFailure_LinksToTheEndpointWithTheStatusCode()
    {
        var key = EndpointHandler.GetProxyEndpoints().Keys.Single(k => k.Contains("HealthItemsTest"));
        var items = WebUiEndpointExtensions.HealthItems("ProxyEndpoints", Entry(HealthStatus.Unhealthy, new()
        {
            [key] = new { Status = "Unhealthy", StatusCode = 502, ReasonPhrase = "Bad Gateway" },
            ["Gone/Endpoint"] = new { Status = "Unhealthy", Error = "No such host is known. (items.invalid:80)" }
        })).ToDictionary(i => i.Name);

        var item = items.Values.Single(i => i.Name == key);
        Assert.Equal("HTTP 502", item.Problem);
        Assert.Equal("endpoints#proxy/HealthItemsTest/Orders", item.Href);
        Assert.Equal(new ConsoleHealthItem("Gone/Endpoint", "Unhealthy", "Unreachable", null), items["Gone/Endpoint"]);
    }

    // disk space and registered checks carry plain values, only the sql and proxy checks list items
    [Fact]
    public void OtherChecks_HaveNoItems()
    {
        Assert.Empty(WebUiEndpointExtensions.HealthItems("Diskspace", Entry(HealthStatus.Healthy, new() { ["FreeSpace"] = "35%", ["TotalGb"] = 120 })));
        Assert.Empty(WebUiEndpointExtensions.HealthItems("Redis", Entry(HealthStatus.Healthy, new() { ["Latency"] = 3.2 })));
    }

    // a report served while the first run holds the lock is a placeholder, never a healthy verdict
    [Fact]
    public async Task FirstRunInProgress_ReturnsThePlaceholder()
    {
        var release = new TaskCompletionSource<HealthReport>();
        var inner = new Mock<MsHealthCheckService>();
        inner.Setup(s => s.CheckHealthAsync(It.IsAny<Func<HealthCheckRegistration, bool>?>(), It.IsAny<CancellationToken>()))
            .Returns(release.Task);
        var service = new PortwayApi.Services.HealthCheckService(inner.Object, TimeSpan.FromHours(1));

        var first = service.CheckHealthAsync(TestContext.Current.CancellationToken);
        var during = await service.CheckHealthAsync(TestContext.Current.CancellationToken);
        release.SetResult(new HealthReport(new Dictionary<string, HealthReportEntry>(), TimeSpan.Zero));

        Assert.Same(PortwayApi.Services.HealthCheckService.Checking, during);
        Assert.NotSame(PortwayApi.Services.HealthCheckService.Checking, await first);
    }

    [Fact]
    public async Task Refresh_RunsTheChecksAgainOncePerInterval()
    {
        var inner = new Mock<MsHealthCheckService>();
        inner.Setup(s => s.CheckHealthAsync(It.IsAny<Func<HealthCheckRegistration, bool>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HealthReport(new Dictionary<string, HealthReportEntry>(), TimeSpan.Zero));
        var service = new PortwayApi.Services.HealthCheckService(inner.Object, TimeSpan.FromHours(1));

        await service.CheckHealthAsync(TestContext.Current.CancellationToken);
        await service.CheckHealthAsync(TestContext.Current.CancellationToken);
        await service.RefreshAsync(TimeSpan.Zero, TestContext.Current.CancellationToken);
        await service.RefreshAsync(TimeSpan.FromHours(1), TestContext.Current.CancellationToken);

        inner.Verify(s => s.CheckHealthAsync(It.IsAny<Func<HealthCheckRegistration, bool>?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
