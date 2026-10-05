using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PortwayApi.Services;
using PortwayApi.Tests.Base;
using Xunit;

namespace PortwayApi.Tests.Middleware;

public class RequestMetricsTests : ApiTestBase
{
    private MetricsService Metrics => _factory.Services.GetRequiredService<MetricsService>();

    private List<HealthRow> Rows() => Metrics.GetHealth(TimeSpan.FromHours(1), new HealthFilter()).Breakdown;

    [Fact]
    public async Task UnhandledException_IsRecordedAsFailure()
    {
        SetAllowedEnvironments("500");
        _mockTokenService.Setup(s => s.GetTokenDetailsByTokenAsync("boom")).ThrowsAsync(new InvalidOperationException("boom"));
        Metrics.Clear();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/500/Inventory/Products");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "boom");
        await _client.SendAsync(request, TestContext.Current.CancellationToken);

        var row = Assert.Single(Rows());
        Assert.Equal(1, row.Summary.Failures);
        Assert.Equal("Inventory/Products", row.Endpoint);
    }

    [Fact]
    public async Task NamespacedEndpoint_IsLabelledByConfiguredName()
    {
        SetAllowedEnvironments("500");
        Metrics.Clear();

        await _client.GetAsync("/api/500/Inventory/Products", TestContext.Current.CancellationToken);

        var row = Assert.Single(Rows());
        Assert.Equal(("500", "Inventory/Products", "", "GET"), (row.Environment, row.Endpoint, row.Version, row.Method));
        Assert.NotNull(row.Summary.P50);
    }

    [Fact]
    public async Task UnknownEnvironmentEndpointAndMethod_AreNotRecordedAsLabels()
    {
        SetAllowedEnvironments("500");
        Metrics.Clear();

        await _client.GetAsync("/api/wp-admin/setup.php", TestContext.Current.CancellationToken);
        await _client.GetAsync("/api/500/Nonexistent/Probe", TestContext.Current.CancellationToken);
        await _client.SendAsync(new HttpRequestMessage(new HttpMethod("PROBE"), "/api/500/Nonexistent"), TestContext.Current.CancellationToken);

        var options = Metrics.GetHealth(TimeSpan.FromHours(1), new HealthFilter()).Options;
        Assert.Equal(["", "500"], options.Environments);
        Assert.Equal([""], options.Endpoints);
        Assert.Equal(["GET", "OTHER"], options.Methods);
    }

    [Fact]
    public async Task NonApiPaths_AreNotCountedAsApiTraffic()
    {
        Metrics.Clear();

        await _client.GetAsync("/docs", TestContext.Current.CancellationToken);

        Assert.Empty(Rows());
    }
}
