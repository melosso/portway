using PortwayApi.Services;
using Xunit;

namespace PortwayApi.Tests.Services;

public class MetricsHealthTests
{
    private static MetricsService Seed(params (int Status, int Ms, string Endpoint, string Method, string Source)[] requests)
    {
        var metrics = new MetricsService();
        foreach (var r in requests)
            metrics.Record(r.Status, r.Method, r.Source, r.Endpoint, "500", "", r.Ms);
        return metrics;
    }

    [Fact]
    public void Summary_CountsOnly5xxAsFailures_AndReadsNearestRankPercentiles()
    {
        var requests = Enumerable.Range(1, 100)
            .Select(i => (Status: i <= 2 ? 500 : i <= 7 ? 404 : 200, Ms: i * 10, Endpoint: "Orders", Method: "GET", Source: "api"))
            .ToArray();

        var summary = Seed(requests).GetHealth(TimeSpan.FromHours(1), new HealthFilter()).Summary;

        Assert.Equal(100, summary.Total);
        Assert.Equal(2, summary.Failures);
        Assert.Equal(5, summary.ClientErrors);
        Assert.Equal(0.98, summary.SuccessRate);
        Assert.Equal(500, summary.P50);
        Assert.Equal(900, summary.P90);
        Assert.Equal(950, summary.P95);
        Assert.Equal(990, summary.P99);
        Assert.Equal(505, summary.MeanMs);
    }

    [Fact]
    public void Health_ExcludesConsoleTraffic_AndAppliesFilters()
    {
        var metrics = Seed(
            (200, 10, "Orders", "GET", "api"),
            (503, 900, "Orders", "POST", "api"),
            (200, 20, "Customers", "GET", "api"),
            (500, 5, "", "GET", "ui"));

        var all = metrics.GetHealth(TimeSpan.FromHours(1), new HealthFilter());
        var orders = metrics.GetHealth(TimeSpan.FromHours(1), new HealthFilter(Endpoint: "orders"));
        var posts = metrics.GetHealth(TimeSpan.FromHours(1), new HealthFilter(Method: "POST"));

        Assert.Equal(3, all.Summary.Total);
        Assert.Equal(1, all.Summary.Failures);
        Assert.Equal(2, orders.Summary.Total);
        Assert.Equal(1, posts.Summary.Total);
        Assert.Equal(["Customers", "Orders"], all.Options.Endpoints);
        Assert.Equal("POST", all.Breakdown[0].Method);
    }

    [Fact]
    public void Health_BinsLatency_WithOverflowBin()
    {
        var metrics = Seed((200, 3, "A", "GET", "api"), (200, 100, "A", "GET", "api"), (200, 60_000, "A", "GET", "api"));

        var latency = metrics.GetHealth(TimeSpan.FromHours(1), new HealthFilter()).Latency;

        Assert.Equal(1, latency.Single(b => b.UpperMs == 5).Count);
        Assert.Equal(1, latency.Single(b => b.UpperMs == 100).Count);
        Assert.Equal(1, latency.Single(b => b.UpperMs is null).Count);
    }

    [Fact]
    public void Health_EmptyWindow_ReportsNullRatesAndPercentiles()
    {
        var summary = new MetricsService().GetHealth(TimeSpan.FromHours(1), new HealthFilter()).Summary;

        Assert.Equal(0, summary.Total);
        Assert.Null(summary.SuccessRate);
        Assert.Null(summary.P95);
    }

    [Fact]
    public void Clear_DropsEveryRecordedRequest()
    {
        var metrics = Seed((500, 10, "Orders", "GET", "api"));

        metrics.Clear();

        Assert.Equal(0, metrics.GetHealth(TimeSpan.FromDays(30), new HealthFilter()).Summary.Total);
        Assert.Equal(0, metrics.GetSnapshot("30d").Total);
    }

    [Fact]
    public void Clear_KeepsRequestsRecordedAfterIt()
    {
        var metrics = Seed((500, 10, "Orders", "GET", "api"));

        var clearedAt = metrics.Clear();
        metrics.Record(200, "GET", "api", "Orders", "500", "", 5);

        Assert.True(clearedAt <= DateTime.UtcNow);
        Assert.Equal(1, metrics.GetHealth(TimeSpan.FromDays(30), new HealthFilter()).Summary.Total);
    }

    [Fact]
    public void Health_IgnoresDurationlessRowsInPercentiles()
    {
        var metrics = new MetricsService();
        metrics.Record(200, "GET", "api", "Orders", "500", "", null);
        metrics.Record(200, "GET", "api", "Orders", "500", "", 40);

        var summary = metrics.GetHealth(TimeSpan.FromHours(1), new HealthFilter()).Summary;

        Assert.Equal(2, summary.Total);
        Assert.Equal(40, summary.P99);
    }
}
