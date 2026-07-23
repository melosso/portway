using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using PortwayApi.Services.Telemetry;
using Xunit;

namespace PortwayApi.Tests.Services;

/// <summary>Tests for Prometheus provider configuration binding and registration</summary>
public class PrometheusOptionsTests
{
    // Option binding

    [Fact]
    public void PrometheusOptions_DefaultsToMetricsPath()
    {
        var options = new TelemetryOptions();

        Assert.Equal(TelemetryProvider.None, options.Provider);
        Assert.Equal("/metrics", options.Prometheus.Path);
        Assert.Null(options.ActiveMetricsPath);
    }

    [Fact]
    public void PrometheusOptions_BindsProviderAndPathFromConfig()
    {
        var config = BuildConfig(new()
        {
            ["Telemetry:Provider"]        = "Prometheus",
            ["Telemetry:Prometheus:Path"] = "/internal/metrics"
        });

        var options = config.GetSection("Telemetry").Get<TelemetryOptions>()!;

        Assert.Equal(TelemetryProvider.Prometheus, options.EffectiveProvider);
        Assert.Equal("/internal/metrics", options.ActiveMetricsPath);
    }

    [Fact]
    public void ActiveMetricsPath_NullForOtlpProvider()
    {
        var config = BuildConfig(new() { ["Telemetry:Provider"] = "Otlp" });

        var options = config.GetSection("Telemetry").Get<TelemetryOptions>()!;

        Assert.Null(options.ActiveMetricsPath);
    }

    // Service registration

    [Fact]
    public void AddPortwayTelemetry_PrometheusProvider_RegistersMeterProvider()
    {
        var config = BuildConfig(new() { ["Telemetry:Provider"] = "Prometheus" });

        var services = new ServiceCollection();
        services.AddPortwayTelemetry(config, "1.0.0");
        var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<MeterProvider>());
    }

    [Fact]
    public void AddPortwayTelemetry_NoProvider_DoesNotRegisterMeterProvider()
    {
        var config = BuildConfig(new() { ["Telemetry:Provider"] = "None" });

        var services = new ServiceCollection();
        services.AddPortwayTelemetry(config, "1.0.0");
        var provider = services.BuildServiceProvider();

        Assert.Null(provider.GetService<MeterProvider>());
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
}

/// <summary>Integration tests exercising the scrape endpoint against the full pipeline</summary>
[Collection("Integration")]
public class PrometheusScrapeEndpointTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _authDbPath;
    private readonly string _mcpDbPath;

    public PrometheusScrapeEndpointTests()
    {
        var id = Guid.NewGuid().ToString("N");
        _authDbPath = Path.Combine(Path.GetTempPath(), $"portway_test_{id}_auth.db");
        _mcpDbPath  = Path.Combine(Path.GetTempPath(), $"portway_test_{id}_mcp.db");

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                // UseSetting applies before Program reads builder.Configuration;
                // ConfigureAppConfiguration would land too late for service registration
                builder.UseSetting("Mcp:Enabled", "false");
                builder.UseSetting("Telemetry:Provider", "Prometheus");

                builder.ConfigureTestServices(services =>
                {
                    // Isolate SQLite databases per test instance to prevent file-lock races
                    services.AddDbContext<PortwayApi.Auth.AuthDbContext>(opts =>
                        opts.UseSqlite($"Data Source={_authDbPath}"),
                        ServiceLifetime.Scoped, ServiceLifetime.Scoped);
                    services.AddDbContextFactory<PortwayApi.Services.Mcp.McpConfigDbContext>(opts =>
                        opts.UseSqlite($"Data Source={_mcpDbPath}"));

                    services.AddLogging(logging =>
                    {
                        logging.ClearProviders();
                        logging.SetMinimumLevel(LogLevel.Warning);
                    });
                });
            });

        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task MetricsEndpoint_WhenEnabled_ReturnsPrometheusExposition()
    {
        var response = await _client.GetAsync("/metrics");

        Assert.True(response.IsSuccessStatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("# TYPE", content);
    }

    [Fact]
    public async Task MetricsEndpoint_AfterApiRequest_ExposesRequestDurationHistogram()
    {
        // Any /api request (even a 404) flows through the request metrics middleware
        await _client.GetAsync("/api/600/nonexistent");

        var response = await _client.GetAsync("/metrics");
        var content  = await response.Content.ReadAsStringAsync();

        Assert.Contains("portway_request_duration", content);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        if (File.Exists(_authDbPath)) File.Delete(_authDbPath);
        if (File.Exists(_mcpDbPath))  File.Delete(_mcpDbPath);
        GC.SuppressFinalize(this);
    }
}

/// <summary>Verifies the scrape endpoint stays unmapped when Prometheus is not the provider</summary>
public class PrometheusDisabledTests : Base.ApiTestBase
{
    [Fact]
    public async Task MetricsEndpoint_WhenDisabled_Returns404()
    {
        var response = await _client.GetAsync("/metrics");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }
}
