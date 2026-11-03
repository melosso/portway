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
            .Where(w => w.Kind != "clear" && !values.ContainsKey(w.Key))
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
}
