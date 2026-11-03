using PortwayApi.Classes;
using PortwayApi.Services;
using PortwayApi.Services.Providers;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace PortwayApi.Tests.Services;

[Collection("Integration")]
public sealed class SqlMetadataFailureLogTests
{
    private sealed class CollectingSink : ILogEventSink
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<LogEvent> _events = new();

        public IReadOnlyCollection<LogEvent> Events => _events;

        public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);
    }

    [Fact]
    public async Task UnreachableEnvironment_LogsOneSummaryWarning()
    {
        var sink = new CollectingSink();
        var previous = Log.Logger;
        Log.Logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
        try
        {
            var factory = new SqlProviderFactory([new SqliteProvider()]);
            var service = new SqlMetadataService(new SqlConnectionPoolService(new SqlPoolingOptions(0, 10, 15, true, "test"), factory), factory);
            var endpoints = Enumerable.Range(1, 3).ToDictionary(
                i => $"Unreachable/Items{i}",
                i => new EndpointDefinition { DatabaseObjectName = $"Items{i}", DatabaseObjectType = "Table", AllowedEnvironments = ["dead-env"] });

            await service.InitializeAsync(
                endpoints,
                new EnvironmentSettings(),
                _ => throw new InvalidOperationException("connection refused"),
                TestContext.Current.CancellationToken);

            var failures = sink.Events
                .Where(e => e.Level >= LogEventLevel.Warning && e.RenderMessage().Contains("Unreachable/Items"))
                .ToList();
            var summary = Assert.Single(failures);
            Assert.Contains("3", summary.RenderMessage());
            Assert.Contains("connection refused", summary.RenderMessage());
        }
        finally
        {
            Log.Logger = previous;
        }
    }
}
