using System.Collections.Concurrent;
using PortwayApi.Services.Configuration;
using PortwayApi.Tests.Support;
using Xunit;

namespace PortwayApi.Tests.Services;

/// <summary>
/// The watcher handles a burst of writes once, after the last write
/// </summary>
public sealed class FileWatchPumpTests
{
    private sealed class RecordingPump(string path) : FileWatchPump(path, "Test")
    {
        public ConcurrentQueue<string> Seen { get; } = new();

        protected override TimeSpan DebounceTime => TimeSpan.FromMilliseconds(300);

        protected override Task HandleFileChangeAsync(string filePath, WatcherChangeTypes changeType)
        {
            Seen.Enqueue(File.Exists(filePath) ? File.ReadAllText(filePath) : "");
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task BurstOfWrites_HandledOnceWithFinalContent()
    {
        using var temp = new TempDirectory("pump");
        var file = temp.Combine("entity.json");
        File.WriteAllText(file, "{}");
        var pump = new RecordingPump(temp.Path);
        await pump.StartAsync(TestContext.Current.CancellationToken);

        File.WriteAllText(file, "");
        await Task.Delay(50, TestContext.Current.CancellationToken);
        File.WriteAllText(file, "{\"v\":2}");
        await Task.Delay(1500, TestContext.Current.CancellationToken);

        await pump.StopAsync(TestContext.Current.CancellationToken);
        pump.Dispose();
        Assert.Equal(["{\"v\":2}"], pump.Seen);
    }
}
