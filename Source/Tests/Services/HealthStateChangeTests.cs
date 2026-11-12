using PortwayApi.Services;
using Xunit;

namespace PortwayApi.Tests.Services;

public class HealthStateChangeTests
{
    [Fact]
    public void FirstHealthyCheck_IsNotAChange()
    {
        var last = "";

        Assert.False(HealthCheckService.StateChanged(ref last, [], out _));
    }

    [Fact]
    public void SameNamesInAnotherOrder_IsNotAChange()
    {
        var last = "";
        Assert.True(HealthCheckService.StateChanged(ref last, ["700", "500"], out var current));
        Assert.Equal("500, 700", current);

        Assert.False(HealthCheckService.StateChanged(ref last, ["500", "700"], out _));
    }

    [Fact]
    public void RecoveryAndNewFailure_AreChanges()
    {
        var last = "500";

        Assert.True(HealthCheckService.StateChanged(ref last, ["500", "Synergy"], out _));
        Assert.True(HealthCheckService.StateChanged(ref last, [], out var current));
        Assert.Equal("", current);
    }
}
