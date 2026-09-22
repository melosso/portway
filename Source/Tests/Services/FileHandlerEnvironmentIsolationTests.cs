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
/// Download/delete must reject a file ID whose encoded environment differs from the caller's authorized route environment
/// </summary>
public class FileHandlerEnvironmentIsolationTests : IDisposable
{
    private readonly string _storageDir;
    private readonly FileHandlerService _handler;

    public FileHandlerEnvironmentIsolationTests()
    {
        _storageDir = Path.Combine(Path.GetTempPath(), $"portway_fileenv_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_storageDir);

        var cacheOptions = new CacheOptions { Enabled = false };
        var cacheManager = new CacheManager(
            new StaticOptionsMonitor<CacheOptions>(cacheOptions),
            new MemoryCacheProvider(Options.Create(cacheOptions)),
            new MetricsService(),
            new PortwayMetrics());

        var fileOptions = new StaticOptionsMonitor<FileStorageOptions>(new FileStorageOptions
        {
            StorageDirectory = _storageDir,
            UseMemoryCache = true,
            MaxTotalMemoryCacheMB = 512,
        });

        _handler = new FileHandlerService(fileOptions, cacheManager, Serilog.Log.Logger);
    }

    public void Dispose()
    {
        _handler.Dispose();
        if (Directory.Exists(_storageDir)) Directory.Delete(_storageDir, recursive: true);
    }

    [Fact]
    public async Task DownloadFileAsync_EnvironmentMismatch_ThrowsUnauthorized()
    {
        var fileId = await _handler.UploadFileAsync("prod", "invoice.pdf", new MemoryStream(Encoding.UTF8.GetBytes("secret")));

        // The route the caller was actually authorized for is "dev", not "prod"
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _handler.DownloadFileAsync(fileId, "dev"));
    }

    [Fact]
    public async Task DownloadFileAsync_EnvironmentMatches_Succeeds()
    {
        var fileId = await _handler.UploadFileAsync("prod", "invoice.pdf", new MemoryStream(Encoding.UTF8.GetBytes("secret")));

        var (stream, filename, _) = await _handler.DownloadFileAsync(fileId, "prod");

        Assert.Equal("invoice.pdf", filename);
        stream.Dispose();
    }

    [Fact]
    public async Task DeleteFileAsync_EnvironmentMismatch_ThrowsUnauthorizedAndLeavesFileIntact()
    {
        var fileId = await _handler.UploadFileAsync("prod", "invoice.pdf", new MemoryStream(Encoding.UTF8.GetBytes("secret")));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _handler.DeleteFileAsync(fileId, "dev"));

        // Still downloadable under its real environment, so the mismatched delete attempt did not touch it
        var (stream, filename, _) = await _handler.DownloadFileAsync(fileId, "prod");
        Assert.Equal("invoice.pdf", filename);
        stream.Dispose();
    }
}
