using Microsoft.Extensions.Configuration;
using PortwayApi.Services.Configuration;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace PortwayApi.Tests.Services;

[Collection("Integration")]
public sealed class SettingsStartupValidationTests
{
    private sealed class CollectingSink : ILogEventSink
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<LogEvent> _events = new();

        public IReadOnlyCollection<LogEvent> Events => _events;

        public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);
    }

    private static IReadOnlyCollection<LogEvent> Validate(Dictionary<string, string?> overrides)
    {
        var config = new ConfigurationManager();
        config.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
        config.AddInMemoryCollection(overrides);

        var sink = new CollectingSink();
        var previous = Log.Logger;
        Log.Logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
        try
        {
            config.ValidateConsoleSettings();
            return sink.Events.Where(e => e.Level >= LogEventLevel.Warning).ToList();
        }
        finally
        {
            Log.Logger = previous;
        }
    }

    [Fact]
    public void ShippedSettings_Pass() => Assert.Empty(Validate([]));

    [Fact]
    public void ReservedMcpPath_FailsStart()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Validate(new() { ["Mcp:Path"] = "/api" }));
        Assert.Contains("Mcp:Path", error.Message);
    }

    [Fact]
    public void McpPathOnPrometheusDefault_FailsStart()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Validate(new() { ["Mcp:Path"] = "/metrics", ["Telemetry:Prometheus:Path"] = null }));
        Assert.Contains("Mcp:Path", error.Message);
    }

    [Fact]
    public void UrlInAllowedHosts_FailsStart()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Validate(new() { ["AllowedHosts"] = "http://x" }));
        Assert.Contains("AllowedHosts", error.Message);
    }

    [Fact]
    public void OutOfRangeLimit_WarnsOnce()
    {
        var warning = Assert.Single(Validate(new() { ["RateLimiting:IpLimit"] = "2000000" }));
        Assert.Contains("RateLimiting:IpLimit", warning.RenderMessage());
    }

    [Fact]
    public void AbsoluteStorageDirectory_Passes() =>
        Assert.Empty(Validate(new() { ["FileStorage:StorageDirectory"] = "/data/files" }));

    [Theory]
    [InlineData("RateLimiting:IpLimit", "abc")]
    [InlineData("RateLimiting:IpLimit", "3000000000")]
    [InlineData("Caching:Enabled", "yes")]
    [InlineData("DatabaseMaintenance:FreePageRatioThreshold", "half")]
    public void UnparsableValue_FailsStart(string key, string value)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Validate(new() { [key] = value }));
        Assert.Contains(key, error.Message);
    }

    [Fact]
    public void FileSizeAboveTheIntRange_StartsWithAWarning()
    {
        var warning = Assert.Single(Validate(new() { ["FileStorage:MaxFileSizeBytes"] = "5368709120" }));
        Assert.Contains("FileStorage:MaxFileSizeBytes", warning.RenderMessage());
    }

    [Theory]
    [InlineData("portway")]
    [InlineData("/portway/")]
    [InlineData("/")]
    public void PathBaseThatWorksAtRuntime_Passes(string value) =>
        Assert.Empty(Validate(new() { ["PathBase"] = value }));

    [Fact]
    public void EmptySqlitePath_Passes() =>
        Assert.Empty(Validate(new() { ["RequestTrafficLogging:SqlitePath"] = "" }));

    [Fact]
    public void EmptyAllowedHosts_Passes() =>
        Assert.Empty(Validate(new() { ["AllowedHosts"] = "" }));

    [Fact]
    public void ApplicationNameOutsideTheConsoleFormat_StartsWithAWarning()
    {
        var warning = Assert.Single(Validate(new() { ["SqlConnectionPooling:ApplicationName"] = "Portway: prod" }));
        Assert.Contains("SqlConnectionPooling:ApplicationName", warning.RenderMessage());
    }

    [Theory]
    [InlineData("/sm")]
    [InlineData("/sm/mcp")]
    [InlineData("/metrics/mcp")]
    public void McpPathWhereTokenAuthIsSkipped_FailsStart(string path)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Validate(new() { ["Mcp:Path"] = path, ["Telemetry:Prometheus:Path"] = "/metrics" }));
        Assert.Contains("Mcp:Path", error.Message);
    }

    [Fact]
    public void WideKnownNetwork_FailsStart()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Validate(new() { ["ForwardedHeaders:KnownNetworks:0"] = "0.0.0.0/0" }));
        Assert.Contains("ForwardedHeaders:KnownNetworks", error.Message);
    }
}
