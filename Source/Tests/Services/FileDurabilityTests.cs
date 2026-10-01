using System.Text;
using Microsoft.Extensions.Options;
using PortwayApi.Services;
using PortwayApi.Services.Caching;
using PortwayApi.Services.Files;
using PortwayApi.Services.Telemetry;
using PortwayApi.Tests.Support;
using Xunit;

namespace PortwayApi.Tests.Services;

/// <summary>
/// An acknowledged upload is on disk before the call returns
/// </summary>
public sealed class FileDurabilityTests : IDisposable
{
    private readonly string _storageDir = Path.Combine(Path.GetTempPath(), $"portway_durable_{Guid.NewGuid():N}");
    private readonly FileHandlerService _handler;

    public FileDurabilityTests()
    {
        Directory.CreateDirectory(_storageDir);
        var cacheOptions = new CacheOptions { Enabled = false };
        var cacheManager = new CacheManager(
            new StaticOptionsMonitor<CacheOptions>(cacheOptions),
            new MemoryCacheProvider(Options.Create(cacheOptions)),
            new MetricsService(),
            new PortwayMetrics());
        _handler = new FileHandlerService(
            new StaticOptionsMonitor<FileStorageOptions>(new FileStorageOptions { StorageDirectory = _storageDir, UseMemoryCache = true, MaxTotalMemoryCacheMB = 64 }),
            cacheManager,
            Serilog.Log.Logger);
    }

    public void Dispose()
    {
        _handler.Dispose();
        Directory.Delete(_storageDir, recursive: true);
    }

    private static MemoryStream Body(string text) => new(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task UploadIsOnDiskBeforeReturn()
    {
        await _handler.UploadFileAsync("prod", "orders", "march.csv", Body("a,b,c"));

        Assert.Equal("a,b,c", await File.ReadAllTextAsync(Path.Combine(_storageDir, "prod", "orders", "march.csv"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OverwriteReplacesDiskAndCache()
    {
        var fileId = await _handler.UploadFileAsync("prod", "march.csv", Body("old"));
        var (first, _, _) = await _handler.DownloadFileAsync(fileId, "prod");
        first.Dispose();

        await _handler.UploadFileAsync("prod", "march.csv", Body("new"), overwrite: true);

        var (second, _, _) = await _handler.DownloadFileAsync(fileId, "prod");
        Assert.Equal("new", new StreamReader(second).ReadToEnd());
        Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(_storageDir, "prod", "march.csv"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NoPartialFilesRemain()
    {
        await _handler.UploadFileAsync("prod", "a.csv", Body("x"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.UploadFileAsync("prod", "a.csv", Body("y")));

        Assert.Empty(Directory.EnumerateFiles(_storageDir, "*.partial", SearchOption.AllDirectories));
        Assert.Equal("x", await File.ReadAllTextAsync(Path.Combine(_storageDir, "prod", "a.csv"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConcurrentCreateHasOneWinner()
    {
        const int callers = 8;
        using var barrier = new Barrier(callers);
        var outcomes = await Task.WhenAll(Enumerable.Range(0, callers).Select(i => Task.Run(async () =>
        {
            barrier.SignalAndWait(TestContext.Current.CancellationToken);
            try
            {
                await _handler.UploadFileAsync("prod", "race.csv", Body($"caller {i}"));
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        })));

        Assert.Single(outcomes, won => won);
    }

    [Fact]
    public void PartialFilesDeletedOnStart()
    {
        var partial = Path.Combine(_storageDir, "prod", "a.csv.0123.partial");
        Directory.CreateDirectory(Path.GetDirectoryName(partial)!);
        File.WriteAllText(partial, "half");

        FileHandlerService.DeletePartialFiles(_storageDir);

        Assert.False(File.Exists(partial));
    }

    [Fact]
    public async Task ListDuringUploads()
    {
        var cacheOptions = new CacheOptions { Enabled = true };
        using var handler = new FileHandlerService(
            new StaticOptionsMonitor<FileStorageOptions>(new FileStorageOptions { StorageDirectory = Path.Combine(_storageDir, "cached") }),
            new CacheManager(new StaticOptionsMonitor<CacheOptions>(cacheOptions), new MemoryCacheProvider(Options.Create(cacheOptions)), new MetricsService(), new PortwayMetrics()),
            Serilog.Log.Logger);
        await handler.ListFilesAsync("prod");
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var uploads = Task.Run(async () =>
        {
            for (var i = 0; !stop.IsCancellationRequested; i++)
                await handler.UploadFileAsync("prod", $"f{i}.csv", Body("x"));
        }, TestContext.Current.CancellationToken);
        var lists = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
                (await handler.ListFilesAsync("prod")).ToList();
        }));

        await Task.WhenAll(lists.Append(uploads));
    }
}
