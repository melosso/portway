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
    private const string AdminKey = "prometheus-test-admin-key-0123456789-0123456789";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _authDbPath;
    private readonly string _mcpDbPath;

    // Mints a session cookie in the same format WebUiEndpoints.GenerateToken produces
    private static string MintSessionCookie()
    {
        var expiry = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds().ToString();
        var signingKey = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(AdminKey));
        using var hmac = new System.Security.Cryptography.HMACSHA256(signingKey);
        var sig = Convert.ToBase64String(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(expiry)));
        return Uri.EscapeDataString($"{expiry}.{sig}");
    }

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
                builder.UseSetting("WebUi:AdminApiKey", AdminKey);

                builder.ConfigureTestServices(services =>
                {
                    // TestServer leaves RemoteIpAddress null; the Web UI network gate rejects that.
                    // A startup filter runs before app middleware and stamps loopback on every request
                    services.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter>(new LoopbackRemoteIpStartupFilter());

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
    public async Task SettingsApi_ReportsActivePrometheusProvider()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/ui/api/settings");
        request.Headers.Add("Cookie", $"portway_auth={MintSessionCookie()}");
        var response = await _client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var telemetry = doc.RootElement.GetProperty("telemetry");

        Assert.Equal("Prometheus", telemetry.GetProperty("provider").GetString());
        Assert.Equal("/metrics", telemetry.GetProperty("prometheus_path").GetString());
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

/// <summary>Stamps loopback as the remote IP so the Web UI network gate admits TestServer requests</summary>
file sealed class LoopbackRemoteIpStartupFilter : Microsoft.AspNetCore.Hosting.IStartupFilter
{
    public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next) =>
        app =>
        {
            app.Use(inner => async ctx =>
            {
                ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
                await inner(ctx);
            });
            next(app);
        };
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
