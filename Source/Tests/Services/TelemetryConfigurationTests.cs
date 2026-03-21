using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PortwayApi.Services.Telemetry;
using Xunit;

namespace PortwayApi.Tests.Services;

/// <summary>
/// Tests for telemetry configuration binding and service registration.
///
/// Uses the demo environment (WMS) as a reference configuration.
/// Telemetry is opt-in (Enabled: false by default), so these tests verify
/// both the disabled fast-path and the enabled registration path.
/// </summary>
public class TelemetryConfigurationTests
{
    // ── Option binding ────────────────────────────────────────────────────────

    [Fact]
    public void TelemetryOptions_DefaultsToDisabled()
    {
        var config = BuildConfig(new());

        var options = config.GetSection("Telemetry").Get<TelemetryOptions>() ?? new();

        Assert.False(options.Enabled);
    }

    [Fact]
    public void TelemetryOptions_BindsEnabledAndEndpointFromConfig()
    {
        var config = BuildConfig(new()
        {
            ["Telemetry:Enabled"]      = "true",
            ["Telemetry:OtlpEndpoint"] = "http://otel-collector.internal:4317"
        });

        var options = config.GetSection("Telemetry").Get<TelemetryOptions>()!;

        Assert.True(options.Enabled);
        Assert.Equal("http://otel-collector.internal:4317", options.OtlpEndpoint);
    }

    [Fact]
    public void TelemetryOptions_BindsServiceNameOverride()
    {
        // Demo uses service name override to distinguish environments
        var config = BuildConfig(new()
        {
            ["Telemetry:Enabled"]     = "true",
            ["Telemetry:ServiceName"] = "portway-wms-demo"
        });

        var options = config.GetSection("Telemetry").Get<TelemetryOptions>()!;

        Assert.Equal("portway-wms-demo", options.ServiceName);
    }

    [Fact]
    public void TelemetryOptions_BindsResourceAttributes()
    {
        var config = BuildConfig(new()
        {
            ["Telemetry:Enabled"]            = "true",
            ["Telemetry:ResourceAttributes"] = "deployment.environment=production,host.name=gw01"
        });

        var options = config.GetSection("Telemetry").Get<TelemetryOptions>()!;

        Assert.Equal("deployment.environment=production,host.name=gw01", options.ResourceAttributes);
    }

    [Fact]
    public void TelemetryOptions_NullServiceName_FallsBackToDefault()
    {
        var config = BuildConfig(new() { ["Telemetry:Enabled"] = "true" });

        var options = config.GetSection("Telemetry").Get<TelemetryOptions>()!;

        // When ServiceName is not set, the extension method falls back to PortwayTelemetry.ServiceName
        Assert.Null(options.ServiceName);
        Assert.Equal("Portway.Api", PortwayTelemetry.ServiceName);
    }

    // ── Service registration ──────────────────────────────────────────────────

    [Fact]
    public void AddPortwayTelemetry_WhenDisabled_DoesNotRegisterPortwayMetrics()
    {
        var config = BuildConfig(new() { ["Telemetry:Enabled"] = "false" });

        var services = new ServiceCollection();
        services.AddPortwayTelemetry(config, "1.0.0");
        var provider = services.BuildServiceProvider();

        // No PortwayMetrics registered — callers that resolve it will get null
        Assert.Null(provider.GetService<PortwayMetrics>());
    }

    [Fact]
    public void AddPortwayTelemetry_WhenEnabled_RegistersPortwayMetrics()
    {
        var config = BuildConfig(new()
        {
            ["Telemetry:Enabled"]      = "true",
            ["Telemetry:OtlpEndpoint"] = "http://localhost:4317"
        });

        var services = new ServiceCollection();
        services.AddPortwayTelemetry(config, "1.0.0");
        var provider = services.BuildServiceProvider();

        var metrics = provider.GetService<PortwayMetrics>();
        Assert.NotNull(metrics);

        metrics.Dispose();
    }

    [Fact]
    public void AddPortwayTelemetry_WhenEnabled_PortwayMetricsIsSingleton()
    {
        var config = BuildConfig(new()
        {
            ["Telemetry:Enabled"]      = "true",
            ["Telemetry:OtlpEndpoint"] = "http://localhost:4317"
        });

        var services = new ServiceCollection();
        services.AddPortwayTelemetry(config, "1.0.0");
        var provider = services.BuildServiceProvider();

        var a = provider.GetService<PortwayMetrics>();
        var b = provider.GetService<PortwayMetrics>();

        Assert.Same(a, b);

        a!.Dispose();
    }

    // ── Span name constants (breaking-change guard) ───────────────────────────
    // Span names are part of the telemetry API surface. Renaming them silently
    // would break any dashboards or alerts that downstream teams have built.

    [Fact]
    public void OperationNames_SqlExecute_IsStable()
        => Assert.Equal("portway.sql.execute", PortwayTelemetry.Operations.SqlExecute);

    [Fact]
    public void OperationNames_ProxyForward_IsStable()
        => Assert.Equal("portway.proxy.forward", PortwayTelemetry.Operations.ProxyForward);

    [Fact]
    public void OperationNames_CacheGet_IsStable()
        => Assert.Equal("portway.cache.get", PortwayTelemetry.Operations.CacheGet);

    // ── Metric recording (smoke) ──────────────────────────────────────────────

    [Fact]
    public void PortwayMetrics_CacheHit_DoesNotThrow()
    {
        using var metrics = new PortwayMetrics();
        var ex = Record.Exception(() => metrics.CacheHit());
        Assert.Null(ex);
    }

    [Fact]
    public void PortwayMetrics_CacheMiss_DoesNotThrow()
    {
        using var metrics = new PortwayMetrics();
        var ex = Record.Exception(() => metrics.CacheMiss());
        Assert.Null(ex);
    }

    [Fact]
    public void PortwayMetrics_RequestCompleted_DoesNotThrow()
    {
        // Mirrors realistic values from the demo WMS environment
        using var metrics = new PortwayMetrics();
        var ex = Record.Exception(() =>
            metrics.RequestCompleted("GET", 200, "api", TimeSpan.FromMilliseconds(42)));
        Assert.Null(ex);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
}
