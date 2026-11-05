using Microsoft.Extensions.Configuration;
using PortwayApi.Endpoints;
using PortwayApi.Classes.OpenApi;
using PortwayApi.Services.Configuration;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

public class SettingValuesTests
{
    private static Dictionary<string, object?> Values(Dictionary<string, string?> settings) =>
        WebUiEndpointExtensions.SettingValues(
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), new OpenApiSettings(), "None");

    [Fact]
    public void EveryReadableWritableSetting_HasAValue()
    {
        var values = Values([]);

        var missing = SettingsWriteService.Schema
            .Where(w => w.Kind != SettingKind.Clear && !values.ContainsKey(w.Key))
            .Select(w => w.Key)
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void WriteOnlySeedingKey_IsNeverReturned()
    {
        var values = Values(new() { ["Console:AdminApiKey"] = "seed-secret-123" });

        Assert.False(values.ContainsKey("Console:AdminApiKey"));
        Assert.DoesNotContain("seed-secret-123", values.Values.Select(v => v?.ToString()));
    }

    [Fact]
    public void UnsetSettings_ReturnTheGatewayDefaults()
    {
        var values = Values([]);

        Assert.Equal(true, values["DatabaseMaintenance:Enabled"]);
        Assert.Equal("03:00", values["DatabaseMaintenance:Schedule"]);
        Assert.Equal(7, values["DatabaseMaintenance:Backup:Retention"]);
        Assert.Equal("Information", values["Serilog:MinimumLevel:Default"]);
        Assert.Equal("None", values["Telemetry:Provider"]);
    }

    [Fact]
    public void Secrets_ReportOnlyWhetherTheyAreSet()
    {
        var secrets = new Dictionary<string, string?>
        {
            ["Caching:Redis:ConnectionString"] = "redis:6379,password=secret-1",
            ["RateLimiting:Redis:ConnectionString"] = "redis:6379,password=secret-2",
            ["Proxy:Password"] = "secret-3",
            ["Console:SeedPassword"] = "secret-4",
            ["Console:SeedToken"] = "secret-5"
        };
        var values = Values(secrets);

        foreach (var key in secrets.Keys)
            Assert.Equal(true, values[key]);
        Assert.DoesNotContain(values.Values, v => v?.ToString()?.Contains("secret-") == true);
    }

    [Fact]
    public void DeprecatedNames_AreReadThroughTheAliases()
    {
        var config = new ConfigurationManager();
        config.AddInMemoryCollection(new Dictionary<string, string?> { ["RateLimiting:IpWindow"] = "15" });
        config.AddConfigAliases();

        var values = WebUiEndpointExtensions.SettingValues(config, new OpenApiSettings(), "None");

        Assert.Equal(15, values["RateLimiting:IpWindowSeconds"]);
    }

    private static System.Text.Json.JsonElement TrafficStatus(PortwayApi.Middleware.RequestTrafficLoggingOptions running, Dictionary<string, string?> saved) =>
        System.Text.Json.JsonSerializer.SerializeToElement(WebUiEndpointExtensions.TrafficLoggingStatus(running,
            new ConfigurationBuilder().AddInMemoryCollection(saved).Build()));

    [Fact]
    public void TrafficStatus_ReportsTheRunningBodyCapture()
    {
        var status = TrafficStatus(new() { Enabled = true, CaptureResponseBodies = true },
            new() { ["RequestTrafficLogging:Enabled"] = "true", ["RequestTrafficLogging:CaptureResponseBodies"] = "true" });

        Assert.True(status.GetProperty("running").GetBoolean());
        Assert.False(status.GetProperty("request_bodies").GetBoolean());
        Assert.True(status.GetProperty("response_bodies").GetBoolean());
        Assert.False(status.GetProperty("restart_required").GetBoolean());
    }

    [Fact]
    public void TrafficStatus_IgnoresBodyFlagsWhileLoggingIsOff()
    {
        var status = TrafficStatus(new() { Enabled = false, CaptureRequestBodies = true },
            new() { ["RequestTrafficLogging:CaptureRequestBodies"] = "true" });

        Assert.False(status.GetProperty("request_bodies").GetBoolean());
    }

    [Fact]
    public void TrafficStatus_RequiresARestartWhenTheSavedSettingsDiffer()
    {
        var status = TrafficStatus(new() { Enabled = false },
            new() { ["RequestTrafficLogging:Enabled"] = "true" });

        Assert.True(status.GetProperty("restart_required").GetBoolean());
        Assert.False(status.GetProperty("running").GetBoolean());
    }

    [Fact]
    public void HideFromViewer_ReportsProxyAccountAndCollectorAsPresenceOnly()
    {
        var values = Values(new() { ["Proxy:Username"] = "svc-account", ["Proxy:Domain"] = "CORP", ["Telemetry:Otlp:Endpoint"] = "https://otel.example/?token=secret" });

        WebUiEndpointExtensions.HideFromViewer(values);

        Assert.Equal("Set", values["Proxy:Username"]);
        Assert.Equal("Set", values["Proxy:Domain"]);
        Assert.Equal("Set", values["Telemetry:Otlp:Endpoint"]);
    }

    [Fact]
    public void HideFromViewer_KeepsAnEmptyProxyAccountEmpty()
    {
        var values = Values([]);

        WebUiEndpointExtensions.HideFromViewer(values);

        Assert.Equal("", values["Proxy:Username"]);
    }
}
